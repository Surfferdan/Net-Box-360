// Package main: Xenia streaming bridge.
//
// This file implements the media-plane control API used by the .NET
// XeniaManager API (control plane) to start/stop/inspect a WebRTC capture
// session for an *already running* Xenia process, and the signaling
// WebSocket the browser (Three.js frontend) uses to negotiate the peer
// connection directly with this process.
//
// Design notes (see docs/CLOUDMORPH_ARCHITECTURE.md for the full picture):
//   - Unlike the original cloud-morph demo (which launches its own app via
//     run-app.ps1/run-wine.sh at server boot and blocks the whole process on
//     the first RTP packet), this bridge launches ffmpeg lazily, per session,
//     targeting the window title of a process Xenia's launcher already
//     started. Nothing here blocks the HTTP server from binding immediately.
//   - Only one Xenia session is expected to be "live" at a time (Xenia is a
//     single emulator instance); the session/stream registry still tracks
//     historical sessions so status/idempotency checks work correctly.
//   - Input: keyboard/mouse is injected via the existing winvm/syncinput.exe
//     helper (unchanged wire protocol). Gamepad state received over the
//     WebRTC data channel is mapped to keyboard-equivalent presses as a
//     best-effort bridge; full analog/XInput fidelity requires a virtual
//     controller driver (ViGEmBus) - see remaining risks in the architecture
//     doc. The injector abstraction is designed so that a ViGEmBus-backed
//     implementation can be swapped in later without touching this file.
package main

import (
	"encoding/json"
	"fmt"
	"log"
	"net"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"
	"unsafe"

	"github.com/giongto35/cloud-morph/pkg/common/config"
	crtc "github.com/giongto35/cloud-morph/pkg/core/go/cloudapp/webrtc"
	"github.com/gorilla/mux"
	"github.com/gorilla/websocket"
	"github.com/pion/rtp"
	"github.com/pion/webrtc/v3"
)

const (
	defaultVideoRTPPortBase = 5100
	defaultAudioRTPPortBase = 6100
	syncInputTCPPort        = 9090
	firstPacketTimeout      = 10 * time.Second
	windowResolveTimeout    = 5 * time.Second
	windowResolvePoll       = 250 * time.Millisecond

	// standardGameWidth/Height is the standard resolution the Xenia window
	// is forced to (via syncinput.exe) for every session, and the size the
	// capture/encode pipeline targets. 1080p/16:9 matches how the games are
	// meant to be played and what the browser's video element expects.
	standardGameWidth  = 1920
	standardGameHeight = 1080
)

// ---------------------------------------------------------------------------
// Window title resolution
// ---------------------------------------------------------------------------
//
// ffmpeg's gdigrab requires an *exact* window title match, but emulator
// window titles frequently embed build/version metadata that changes on
// every build (e.g. Xenia Canary's title is of the form
// "Xenia-canary (canary_experimental@<hash> on <date>)"). A statically
// configured substring like "Xenia" would otherwise never match, silently
// breaking capture. resolveWindowTarget finds the current exact title and
// screen rectangle of a visible top-level window whose title contains the
// configured substring so ffmpeg, syncinput.exe, and desktop-region capture
// all target the window that actually exists.
//
// IMPORTANT: capture uses gdigrab's "desktop" mode with an offset/size
// derived from the window's screen rectangle, NOT gdigrab's per-window
// "title=" mode. Per-window title capture does a BitBlt against the
// window's own device context, which reads back solid black for windows
// that render via a hardware-accelerated flip-model swap chain (Direct3D
// 11/12, Vulkan, OpenGL with modern present modes) - exactly how Xenia
// renders. Capturing the desktop region instead reads the DWM-composited
// final screen image (the same bits a screenshot tool would capture),
// which is unaffected by the window's own rendering/present model.

var (
	user32                   = syscall.NewLazyDLL("user32.dll")
	procEnumWindows          = user32.NewProc("EnumWindows")
	procGetWindowTextW       = user32.NewProc("GetWindowTextW")
	procGetWindowTextLengthW = user32.NewProc("GetWindowTextLengthW")
	procIsWindowVisible      = user32.NewProc("IsWindowVisible")
	procGetWindowRect        = user32.NewProc("GetWindowRect")
	procGetClientRect        = user32.NewProc("GetClientRect")
	procClientToScreen       = user32.NewProc("ClientToScreen")
)

// windowRect mirrors the Win32 RECT struct (left, top, right, bottom), all
// in virtual-screen coordinates (may be negative on multi-monitor setups).
type windowRect struct {
	Left, Top, Right, Bottom int32
}

// point mirrors the Win32 POINT struct.
type point struct {
	X, Y int32
}

// windowTarget is what capture/input need to know about the resolved window.
type windowTarget struct {
	Title  string
	Left   int
	Top    int
	Width  int
	Height int
}

// resolveWindowTarget polls for up to windowResolveTimeout for a visible
// window whose title contains substr (case-insensitive), returning its
// exact title and current screen rectangle. Falls back to a target using
// the configured substr as-is (and a zero rect) if no match appears in time
// (e.g. the target process is still starting) so callers can still attempt
// capture and get a clear error rather than a nil-pointer panic.
func resolveWindowTarget(substr string) windowTarget {
	deadline := time.Now().Add(windowResolveTimeout)
	for {
		if target, ok := findVisibleWindowContaining(substr); ok {
			return target
		}
		if time.Now().After(deadline) {
			return windowTarget{Title: substr}
		}
		time.Sleep(windowResolvePoll)
	}
}

// findVisibleWindowContaining enumerates top-level visible windows via
// user32.dll!EnumWindows and returns the first one whose title contains
// substr, along with its current screen rectangle.
//
// IMPORTANT: the rectangle is the window's CLIENT AREA (GetClientRect,
// converted to screen coordinates via ClientToScreen), NOT GetWindowRect
// and NOT DWMWA_EXTENDED_FRAME_BOUNDS. Both of those include the title bar
// and, for Xenia, its menu bar - non-client chrome that is not part of the
// actual game render surface. Capturing that rect meant the stream always
// showed the title bar/menu at the top with the real game picture pushed
// down and clipped at the bottom, i.e. never lined up with the game. The
// client rect is exactly the game's drawable surface, matching what the
// D3D/Vulkan swap chain actually renders into. GetWindowRect is kept only
// as a last-resort fallback if these calls somehow fail.
func findVisibleWindowContaining(substr string) (windowTarget, bool) {
	substrLower := strings.ToLower(substr)
	var found windowTarget
	var matched bool

	cb := syscall.NewCallback(func(hwnd syscall.Handle, _ uintptr) uintptr {
		visible, _, _ := procIsWindowVisible.Call(uintptr(hwnd))
		if visible == 0 {
			return 1 // not visible, keep enumerating
		}
		length, _, _ := procGetWindowTextLengthW.Call(uintptr(hwnd))
		if length == 0 {
			return 1
		}
		buf := make([]uint16, length+1)
		procGetWindowTextW.Call(uintptr(hwnd), uintptr(unsafe.Pointer(&buf[0])), uintptr(length+1))
		title := syscall.UTF16ToString(buf)
		if title == "" {
			return 1
		}
		if !strings.Contains(strings.ToLower(title), substrLower) {
			return 1
		}

		var client windowRect // client rect is relative to the window (Left/Top always 0)
		ret, _, _ := procGetClientRect.Call(uintptr(hwnd), uintptr(unsafe.Pointer(&client)))
		if ret == 0 || (client.Right-client.Left) == 0 || (client.Bottom-client.Top) == 0 {
			// Fall back to the full window rect (still better than nothing).
			var rect windowRect
			if fret, _, _ := procGetWindowRect.Call(uintptr(hwnd), uintptr(unsafe.Pointer(&rect))); fret == 0 {
				return 1 // couldn't read any rect; keep looking for another match
			}
			found = windowTarget{
				Title:  title,
				Left:   int(rect.Left),
				Top:    int(rect.Top),
				Width:  int(rect.Right - rect.Left),
				Height: int(rect.Bottom - rect.Top),
			}
			matched = true
			return 0
		}

		// GetClientRect's top-left is always (0,0); convert to screen
		// coordinates so the offset lines up with gdigrab's desktop-region
		// capture (which uses virtual-screen coordinates).
		origin := point{X: 0, Y: 0}
		procClientToScreen.Call(uintptr(hwnd), uintptr(unsafe.Pointer(&origin)))

		found = windowTarget{
			Title:  title,
			Left:   int(origin.X),
			Top:    int(origin.Y),
			Width:  int(client.Right - client.Left),
			Height: int(client.Bottom - client.Top),
		}
		matched = true
		return 0 // stop enumeration
	})

	_, _, _ = procEnumWindows.Call(cb, 0)
	return found, matched
}

// ---------------------------------------------------------------------------
// Input injection
// ---------------------------------------------------------------------------

// inputInjector abstracts "send this input to the emulator" so the transport
// (syncinput.exe today, a virtual controller driver in the future) can change
// without touching session/webrtc plumbing.
type inputInjector interface {
	SendKey(code int, down bool)
	SendMouseMove(x, y, width, height float32)
	SendMouseButton(isLeft bool, down bool, x, y, width, height float32)
}

// syncInputBridge accepts a single TCP connection from winvm/syncinput.exe
// (spawned per active session, pointed at the target window title) and
// forwards keyboard/mouse events using the same wire protocol as the
// original cloudapp.go implementation.
type syncInputBridge struct {
	mu       sync.Mutex
	listener net.Listener
	conn     net.Conn
	ready    bool
}

func newSyncInputBridge() (*syncInputBridge, error) {
	ln, err := net.Listen("tcp", fmt.Sprintf("127.0.0.1:%d", syncInputTCPPort))
	if err != nil {
		return nil, err
	}

	b := &syncInputBridge{listener: ln}
	go b.acceptLoop()
	go b.pingLoop()
	return b, nil
}

func (b *syncInputBridge) acceptLoop() {
	for {
		conn, err := b.listener.Accept()
		if err != nil {
			log.Println("[xenia-bridge] syncinput listener closed:", err)
			return
		}

		log.Println("[xenia-bridge] syncinput.exe connected")
		b.mu.Lock()
		if b.conn != nil {
			_ = b.conn.Close()
		}
		b.conn = conn
		b.ready = true
		b.mu.Unlock()
	}
}

func (b *syncInputBridge) write(msg string) {
	b.writeBytes([]byte(msg), true)
}

func (b *syncInputBridge) writeBytes(payload []byte, logFailure bool) {
	b.mu.Lock()
	defer b.mu.Unlock()
	if !b.ready || b.conn == nil {
		return
	}
	if _, err := b.conn.Write(payload); err != nil {
		if logFailure {
			log.Println("[xenia-bridge] syncinput write failed:", err)
		}
		b.ready = false
	}
}

func (b *syncInputBridge) pingLoop() {
	ticker := time.NewTicker(2 * time.Second)
	defer ticker.Stop()
	for range ticker.C {
		// syncinput.cpp expects a 1-byte zero packet periodically to keep
		// its internal health-check alive; without this it exits after ~6s.
		b.writeBytes([]byte{0}, false)
	}
}

func (b *syncInputBridge) SendKey(code int, down bool) {
	state := 0
	if down {
		state = 1
	}
	b.write(fmt.Sprintf("K%d,%b|", code, state))
}

func (b *syncInputBridge) SendMouseMove(x, y, width, height float32) {
	b.write(fmt.Sprintf("M%d,%d,%f,%f,%f,%f|", 0, 0, x, y, width, height))
}

func (b *syncInputBridge) SendMouseButton(isLeft bool, down bool, x, y, width, height float32) {
	il := 0
	if isLeft {
		il = 1
	}
	state := 2 // up
	if down {
		state = 1
	}
	b.write(fmt.Sprintf("M%d,%d,%f,%f,%f,%f|", il, state, x, y, width, height))
}

// gamepadKeyMap bridges browser Gamepad API button indexes to WinKey HID
// keybinds used by the current Xenia Netplay config.
// Values are Windows VK keycodes. Multiple entries represent held chords.
var gamepadKeyMap = map[int][]int{
	0:  {186},     // A -> ';' (0xBA)
	1:  {222},     // B -> '\'' (0xDE)
	2:  {76},      // X -> L
	3:  {80},      // Y -> P
	4:  {49},      // LB -> 1
	5:  {51},      // RB -> 3
	6:  {81},      // LT -> Q
	7:  {69},      // RT -> E
	8:  {90},      // Back -> Z
	9:  {88},      // Start -> X
	12: {104},     // D-Pad Up -> Numpad 8
	13: {98},      // D-Pad Down -> Numpad 2
	14: {100},     // D-Pad Left -> Numpad 4
	15: {102},     // D-Pad Right -> Numpad 6
	16: {8},       // Guide -> Backspace (0x08)
}

var gamepadAxisKeyMap = map[string][]int{
	"up":    {73}, // Left stick up -> I
	"down":  {75}, // Left stick down -> K
	"left":  {74}, // Left stick left -> J
	"right": {76}, // Left stick right -> L
}

// ---------------------------------------------------------------------------
// Session registry
// ---------------------------------------------------------------------------

type xeniaSession struct {
	mu sync.Mutex

	sessionID    string
	streamID     string
	gameID       string
	gameTitle    string
	captureMode  string
	targetWindow string
	audioInput   string

	status     string // starting, live, capture-timeout, failed, stopping, stopped
	controller string // connecting, game, offline
	lastError  string
	createdAt  time.Time

	videoPort    int
	audioPort    int
	ffmpegCmd    *exec.Cmd
	audioCmd     *exec.Cmd
	syncInputCmd *exec.Cmd
	udpConn      *net.UDPConn
	audioConn    *net.UDPConn
	rtc          *crtc.WebRTC
	stopOnce     sync.Once
	stopped      chan struct{}
}

func (s *xeniaSession) snapshotStatus() (status, controller, lastError string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.status, s.controller, s.lastError
}

func (s *xeniaSession) setStatus(status string) {
	s.mu.Lock()
	s.status = status
	s.mu.Unlock()
}

func (s *xeniaSession) setControllerStatus(status string) {
	s.mu.Lock()
	s.controller = status
	s.mu.Unlock()
}

func (s *xeniaSession) setError(err string) {
	s.mu.Lock()
	s.status = "failed"
	s.lastError = err
	s.mu.Unlock()
}

// XeniaBridge is the media-plane server: REST control endpoints + WebRTC
// signaling + ffmpeg-based window capture + input injection.
type XeniaBridge struct {
	mu            sync.Mutex
	bySession     map[string]*xeniaSession
	byStream      map[string]*xeniaSession
	nextPort      int
	nextAudioPort int
	ffmpegPath    string
	syncInputPath string
	rtcConfig     crtc.Config
	upgrader      websocket.Upgrader
	syncInput     *syncInputBridge
	startedAt     time.Time
	defaultTitle  string
}

func NewXeniaBridge(cfg config.Config) *XeniaBridge {
	rtcConfig := crtc.DefaultConfig
	rtcConfig.VideoCodec = webrtc.MimeTypeH264
	if cfg.DisableInterceptors {
		rtcConfig.DisableInterceptors = true
	}

	syncInput, err := newSyncInputBridge()
	if err != nil {
		// Non-fatal: video capture still works without keyboard/mouse relay;
		// this is logged loudly because controller-status will stay degraded.
		log.Println("[xenia-bridge] WARNING: failed to start syncinput listener on port 9090:", err)
	}

	defaultTitle := cfg.WindowTitle
	if defaultTitle == "" {
		defaultTitle = "Xenia"
	}

	return &XeniaBridge{
		bySession:     map[string]*xeniaSession{},
		byStream:      map[string]*xeniaSession{},
		nextPort:      defaultVideoRTPPortBase,
		nextAudioPort: defaultAudioRTPPortBase,
		ffmpegPath:    "ffmpeg",
		syncInputPath: filepath.Join("winvm", "syncinput.exe"),
		rtcConfig:     rtcConfig,
		upgrader:      websocket.Upgrader{CheckOrigin: func(r *http.Request) bool { return true }},
		syncInput:     syncInput,
		startedAt:     time.Now(),
		defaultTitle:  defaultTitle,
	}
}

func (b *XeniaBridge) RegisterRoutes(r *mux.Router) {
	r.HandleFunc("/healthz", b.handleHealth).Methods(http.MethodGet)
	r.HandleFunc("/streams", b.handleList).Methods(http.MethodGet)
	r.HandleFunc("/streams/start", b.handleStart).Methods(http.MethodPost)
	r.HandleFunc("/streams/stop", b.handleStop).Methods(http.MethodPost)
	r.HandleFunc("/streams/{id}/status", b.handleStatus).Methods(http.MethodGet)
	r.HandleFunc("/streams/{id}/controller-profile", b.handleControllerProfile).Methods(http.MethodPost)
	r.HandleFunc("/streams/{id}/signal", b.handleSignal)
}

// ---------------------------------------------------------------------------
// REST handlers
// ---------------------------------------------------------------------------

type healthResponse struct {
	Status         string `json:"status"`
	CaptureReady   bool   `json:"captureReady"`
	StreamReady    bool   `json:"streamReady"`
	ActiveSessions int    `json:"activeSessions"`
}

func (b *XeniaBridge) handleHealth(w http.ResponseWriter, r *http.Request) {
	b.mu.Lock()
	active := 0
	live := 0
	for _, s := range b.bySession {
		status, _, _ := s.snapshotStatus()
		if status == "starting" || status == "live" {
			active++
		}
		if status == "live" {
			live++
		}
	}
	b.mu.Unlock()

	writeJSON(w, http.StatusOK, healthResponse{
		Status:         "ready",
		CaptureReady:   true,
		StreamReady:    live > 0,
		ActiveSessions: active,
	})
}

type streamSummary struct {
	StreamID   string `json:"streamId"`
	SessionID  string `json:"sessionId"`
	GameID     string `json:"gameId"`
	Status     string `json:"status"`
	Controller string `json:"controllerStatus"`
	CreatedAt  string `json:"createdAt"`
}

func (b *XeniaBridge) handleList(w http.ResponseWriter, r *http.Request) {
	b.mu.Lock()
	defer b.mu.Unlock()

	summaries := make([]streamSummary, 0, len(b.byStream))
	for _, s := range b.byStream {
		status, controller, _ := s.snapshotStatus()
		summaries = append(summaries, streamSummary{
			StreamID:   s.streamID,
			SessionID:  s.sessionID,
			GameID:     s.gameID,
			Status:     status,
			Controller: controller,
			CreatedAt:  s.createdAt.UTC().Format(time.RFC3339),
		})
	}

	writeJSON(w, http.StatusOK, summaries)
}

type startRequest struct {
	SessionID         string `json:"sessionId"`
	GameID            string `json:"gameId"`
	GameTitle         string `json:"gameTitle"`
	CaptureMode       string `json:"captureMode"`
	TargetWindowTitle string `json:"targetWindowTitle"`
	AudioInputDevice  string `json:"audioInputDevice"`
}

type startResponse struct {
	StreamID          string `json:"streamId"`
	StreamUrl         string `json:"streamUrl"`
	ControllerStatus  string `json:"controllerStatus"`
	Status            string `json:"status"`
	CaptureMode       string `json:"captureMode"`
	TargetWindowTitle string `json:"targetWindowTitle"`
}

// handleStart is idempotent per sessionId: a session already starting/live is
// returned as-is instead of spawning a second capture pipeline. A session
// that previously failed/stopped is replaced with a fresh attempt.
func (b *XeniaBridge) handleStart(w http.ResponseWriter, r *http.Request) {
	var req startRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil || strings.TrimSpace(req.SessionID) == "" {
		http.Error(w, `{"error":"sessionId is required"}`, http.StatusBadRequest)
		return
	}

	targetWindow := req.TargetWindowTitle
	if targetWindow == "" {
		targetWindow = b.defaultTitle
	}

	b.mu.Lock()
	if existing, ok := b.bySession[req.SessionID]; ok {
		status, controller, _ := existing.snapshotStatus()
		if status == "starting" || status == "live" {
			b.mu.Unlock()
			writeJSON(w, http.StatusOK, startResponse{
				StreamID:          existing.streamID,
				StreamUrl:         b.signalURL(existing.streamID),
				ControllerStatus:  controller,
				Status:            status,
				CaptureMode:       existing.captureMode,
				TargetWindowTitle: existing.targetWindow,
			})
			return
		}
		// Stale (failed/stopped): drop it and start clean below.
		delete(b.bySession, req.SessionID)
		delete(b.byStream, existing.streamID)
	}

	streamID := fmt.Sprintf("stream-%s", req.SessionID)
	port := b.nextPort
	audioPort := b.nextAudioPort
	b.nextPort++
	b.nextAudioPort++

	session := &xeniaSession{
		sessionID:    req.SessionID,
		streamID:     streamID,
		gameID:       req.GameID,
		gameTitle:    req.GameTitle,
		captureMode:  defaultString(req.CaptureMode, "desktop"),
		targetWindow: targetWindow,
		audioInput:   defaultString(req.AudioInputDevice, "default"),
		status:       "starting",
		controller:   "connecting",
		createdAt:    time.Now(),
		videoPort:    port,
		audioPort:    audioPort,
		stopped:      make(chan struct{}),
	}

	b.bySession[req.SessionID] = session
	b.byStream[streamID] = session
	b.mu.Unlock()

	go b.runCapture(session)

	writeJSON(w, http.StatusOK, startResponse{
		StreamID:          streamID,
		StreamUrl:         b.signalURL(streamID),
		ControllerStatus:  "connecting",
		Status:            "starting",
		CaptureMode:       session.captureMode,
		TargetWindowTitle: targetWindow,
	})
}

func (b *XeniaBridge) signalURL(streamID string) string {
	return fmt.Sprintf("/streams/%s/signal", streamID)
}

type stopRequest struct {
	StreamID  string `json:"streamId"`
	SessionID string `json:"sessionId"`
}

func (b *XeniaBridge) handleStop(w http.ResponseWriter, r *http.Request) {
	var req stopRequest
	_ = json.NewDecoder(r.Body).Decode(&req)

	b.mu.Lock()
	var session *xeniaSession
	if req.StreamID != "" {
		session = b.byStream[req.StreamID]
	}
	if session == nil && req.SessionID != "" {
		session = b.bySession[req.SessionID]
	}
	b.mu.Unlock()

	if session == nil {
		// Stopping an unknown/already-gone session is not an error: stop must
		// be safe to call repeatedly (idempotent release).
		writeJSON(w, http.StatusOK, map[string]any{"success": true, "status": "stopped"})
		return
	}

	b.stopSession(session, "stopped by request")
	writeJSON(w, http.StatusOK, map[string]any{"success": true, "status": "stopped"})
}

func (b *XeniaBridge) handleStatus(w http.ResponseWriter, r *http.Request) {
	streamID := mux.Vars(r)["id"]

	b.mu.Lock()
	session := b.byStream[streamID]
	b.mu.Unlock()

	if session == nil {
		writeJSON(w, http.StatusOK, map[string]any{"streamId": streamID, "status": "unknown"})
		return
	}

	status, _, lastError := session.snapshotStatus()
	writeJSON(w, http.StatusOK, map[string]any{"streamId": streamID, "status": status, "error": lastError})
}

type controllerProfileRequest struct {
	DeadzonePercent float64        `json:"deadzonePercent"`
	ButtonMap       map[string]int `json:"buttonMap"`
}

// handleControllerProfile is a frontend-facing (not .NET-facing) endpoint:
// the browser posts its resolved deadzone/mapping once connected so future
// gamepad->keyboard bridging can honor per-player preferences. Today it is
// acknowledged and logged; deeper per-session customization is future work.
func (b *XeniaBridge) handleControllerProfile(w http.ResponseWriter, r *http.Request) {
	streamID := mux.Vars(r)["id"]
	var req controllerProfileRequest
	_ = json.NewDecoder(r.Body).Decode(&req)
	log.Printf("[xenia-bridge] controller profile for %s: deadzone=%.2f buttons=%d\n", streamID, req.DeadzonePercent, len(req.ButtonMap))
	writeJSON(w, http.StatusOK, map[string]any{"accepted": true})
}

func writeJSON(w http.ResponseWriter, status int, payload any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(payload)
}

func defaultString(value, fallback string) string {
	if strings.TrimSpace(value) == "" {
		return fallback
	}
	return value
}

// ---------------------------------------------------------------------------
// Capture pipeline (ffmpeg gdigrab -> RTP -> pion WebRTC track)
// ---------------------------------------------------------------------------

func (b *XeniaBridge) runCapture(s *xeniaSession) {
	addr := &net.UDPAddr{IP: net.IPv4zero, Port: s.videoPort}
	conn, err := net.ListenUDP("udp", addr)
	if err != nil {
		log.Println("[xenia-bridge] failed to open RTP listener:", err)
		s.setError("failed to open local RTP listener: " + err.Error())
		return
	}
	s.udpConn = conn

	audioAddr := &net.UDPAddr{IP: net.IPv4zero, Port: s.audioPort}
	audioConn, err := net.ListenUDP("udp", audioAddr)
	if err != nil {
		log.Println("[xenia-bridge] failed to open audio RTP listener:", err)
		s.setError("failed to open local audio RTP listener: " + err.Error())
		_ = conn.Close()
		return
	}
	s.audioConn = audioConn

	// Resolve the exact current window title before invoking syncinput/ffmpeg.
	// The title is stable enough to identify the window, but the rectangle is
	// intentionally resolved again after syncinput.exe has had a chance to
	// normalize the window size so capture uses the final client bounds.
	target := resolveWindowTarget(s.targetWindow)
	resolvedTitle := target.Title
	if resolvedTitle != s.targetWindow {
		log.Printf("[xenia-bridge] resolved window title %q -> %q for session %s\n", s.targetWindow, resolvedTitle, s.sessionID)
	}

	// Spawn a separate ffmpeg process for audio so video capture remains
	// resilient even if audio-device probing fails temporarily.
	audioSource := defaultString(s.audioInput, "default")
	if !strings.EqualFold(audioSource, "default") && !strings.HasPrefix(strings.ToLower(audioSource), "audio=") {
		audioSource = "audio=" + audioSource
	}

	audioArgs := []string{
		"-f", "wasapi",
		"-i", audioSource,
		"-ac", "2",
		"-ar", "48000",
		"-c:a", "libopus",
		"-application", "lowdelay",
		"-b:a", "128k",
		"-f", "rtp", fmt.Sprintf("rtp://127.0.0.1:%d", s.audioPort),
	}
	audioCmd := exec.Command(b.ffmpegPath, audioArgs...)
	s.audioCmd = audioCmd
	if err := audioCmd.Start(); err != nil {
		if !strings.EqualFold(audioSource, "default") {
			log.Printf("[xenia-bridge] WARNING: failed to start audio capture on %q: %v; retrying on default\n", audioSource, err)
			fallbackArgs := []string{
				"-f", "wasapi",
				"-i", "default",
				"-ac", "2",
				"-ar", "48000",
				"-c:a", "libopus",
				"-application", "lowdelay",
				"-b:a", "128k",
				"-f", "rtp", fmt.Sprintf("rtp://127.0.0.1:%d", s.audioPort),
			}
			fallbackCmd := exec.Command(b.ffmpegPath, fallbackArgs...)
			s.audioCmd = fallbackCmd
			if fallbackErr := fallbackCmd.Start(); fallbackErr != nil {
				log.Println("[xenia-bridge] WARNING: failed to start ffmpeg default audio capture:", fallbackErr)
				log.Println("[xenia-bridge] continuing with video-only stream for session", s.sessionID)
			} else {
				log.Printf("[xenia-bridge] audio capture started for session %s (device=%q, audioPort=%d, pid=%d)\n", s.sessionID, "default", s.audioPort, fallbackCmd.Process.Pid)
				go b.forwardAudioRTP(s)
			}
		} else {
			log.Println("[xenia-bridge] WARNING: failed to start ffmpeg audio capture:", err)
			log.Println("[xenia-bridge] continuing with video-only stream for session", s.sessionID)
		}
	} else {
		log.Printf("[xenia-bridge] audio capture started for session %s (device=%q, audioPort=%d, pid=%d)\n", s.sessionID, audioSource, s.audioPort, audioCmd.Process.Pid)
		go b.forwardAudioRTP(s)
	}

	// Spawn syncinput.exe pointed at the same window so keyboard/mouse input
	// forwarded over the data channel actually reaches the emulator. Nothing
	// else launches this binary for a Xenia session (Xenia itself is started
	// by the .NET launcher before /streams/start is ever called), unlike the
	// original run-app.ps1 flow which looped this alongside the demo app.
	if _, statErr := os.Stat(b.syncInputPath); statErr != nil {
		log.Printf("[xenia-bridge] WARNING: syncinput.exe not found at %s; keyboard/mouse input will not reach the emulator for this session\n", b.syncInputPath)
	} else {
		// argv: title, mode, platform, hardcodeIP (unused on Windows), width, height.
		// width/height force the Xenia window to the standard 1920x1080
		// resolution so capture/encode/stream all agree on the same size.
		syncCmd := exec.Command(b.syncInputPath, resolvedTitle, ".", "windows", "", strconv.Itoa(standardGameWidth), strconv.Itoa(standardGameHeight))
		if err := syncCmd.Start(); err != nil {
			log.Println("[xenia-bridge] failed to start syncinput.exe:", err)
		} else {
			s.syncInputCmd = syncCmd
			log.Printf("[xenia-bridge] syncinput.exe started for session %s (pid=%d)\n", s.sessionID, syncCmd.Process.Pid)
		}
	}

	// Resolve the final client-area bounds after syncinput.exe has had a brief
	// chance to normalize the Xenia window to the target 1920x1080 size.
	time.Sleep(500 * time.Millisecond)
	target = resolveWindowTarget(resolvedTitle)

	// -g/-keyint_min force a keyframe (IDR) at least once per second, and
	// -x264-params repeat-headers=1 makes libx264 re-send SPS/PPS before
	// every IDR instead of only once at the very start of the encode.
	// Without this, the browser's WebRTC decoder - which always connects
	// well after ffmpeg has already started producing RTP packets - has no
	// way to ever initialize (no SPS/PPS+IDR in the RTP packets it actually
	// receives), so the <video> element stays solid black forever even
	// though capture, RTP, and the peer connection are all working. With a
	// keyframe guaranteed every ~1s, a late-joining browser is never more
	// than ~1s away from being able to start decoding.

	var args []string
	if target.Width > 0 && target.Height > 0 {
		args = []string{
			"-f", "gdigrab",
			"-framerate", "30",
			"-offset_x", strconv.Itoa(target.Left),
			"-offset_y", strconv.Itoa(target.Top),
			"-video_size", fmt.Sprintf("%dx%d", target.Width, target.Height),
			"-i", "desktop",
			"-pix_fmt", "yuv420p",
			"-c:v", "libx264",
			"-preset", "ultrafast",
			"-tune", "zerolatency",
			"-g", "30", "-keyint_min", "30",
			"-x264-params", "repeat-headers=1",
			"-vf", fmt.Sprintf("scale=%d:-2", standardGameWidth),
			"-f", "rtp", fmt.Sprintf("rtp://127.0.0.1:%d", s.videoPort),
		}
		log.Printf("[xenia-bridge] capturing desktop region for session %s: offset=(%d,%d) size=%dx%d\n", s.sessionID, target.Left, target.Top, target.Width, target.Height)
	} else {
		log.Printf("[xenia-bridge] WARNING: could not resolve window rect for %q; falling back to per-window title capture (may render black for hardware-accelerated windows)\n", resolvedTitle)
		args = []string{
			"-f", "gdigrab",
			"-framerate", "30",
			"-i", "title=" + resolvedTitle,
			"-pix_fmt", "yuv420p",
			"-c:v", "libx264",
			"-preset", "ultrafast",
			"-tune", "zerolatency",
			"-g", "30", "-keyint_min", "30",
			"-x264-params", "repeat-headers=1",
			"-vf", fmt.Sprintf("scale=%d:-2", standardGameWidth),
			"-f", "rtp", fmt.Sprintf("rtp://127.0.0.1:%d", s.videoPort),
		}
	}

	cmd := exec.Command(b.ffmpegPath, args...)
	s.ffmpegCmd = cmd
	if err := cmd.Start(); err != nil {
		log.Println("[xenia-bridge] failed to start ffmpeg capture:", err)
		s.setError("ffmpeg failed to start (is it installed and on PATH?): " + err.Error())
		_ = conn.Close()
		return
	}

	log.Printf("[xenia-bridge] capture started for session %s (window=%q, port=%d, pid=%d)\n", s.sessionID, resolvedTitle, s.videoPort, cmd.Process.Pid)

	// Defense in depth: never block forever waiting for the first RTP packet.
	// If the target window can't be found or ffmpeg can't encode it, surface
	// a clear "capture-timeout" status instead of hanging.
	_ = conn.SetReadDeadline(time.Now().Add(firstPacketTimeout))
	buf := make([]byte, 4096)
	n, _, err := conn.ReadFromUDP(buf)
	if err != nil {
		log.Println("[xenia-bridge] timed out waiting for first RTP packet:", err)
		s.setError("timed out waiting for Xenia window capture (window not found or ffmpeg failed to encode)")
		s.setStatus("capture-timeout")
		b.stopSession(s, "capture-timeout")
		return
	}
	_ = conn.SetReadDeadline(time.Time{})

	firstPacket := &rtp.Packet{}
	if err := firstPacket.Unmarshal(buf[:n]); err != nil {
		log.Println("[xenia-bridge] failed to parse first RTP packet:", err)
	}

	s.setStatus("live")
	log.Printf("[xenia-bridge] first video packet received for session %s; capture is live\n", s.sessionID)

	// Forward RTP into the WebRTC track once a browser has connected via the
	// signaling endpoint and negotiated ICE. Until then, packets are dropped;
	// pion's track write is a no-op/error when no connection is attached, so
	// we guard on s.rtc being non-nil.
	go func() {
		defer conn.Close()
		if err := firstPacket.Unmarshal(buf[:n]); err == nil {
			s.forwardPacket(firstPacket)
		}
		for {
			select {
			case <-s.stopped:
				return
			default:
			}

			// A fresh buffer must be allocated for every packet: rtp.Packet.Unmarshal
			// does not copy the payload, it aliases a slice directly into the buffer
			// we pass in. Packets are handed off to a buffered channel and written out
			// asynchronously by a separate goroutine (webrtc.go's startStreaming), so
			// reusing one buffer across reads let later UDP reads overwrite the bytes
			// of packets still sitting in that channel before they were ever sent -
			// corrupting the H.264 bitstream so the browser could never assemble a
			// single valid frame (transport stats looked perfect; framesReceived
			// stayed at 0 forever).
			readBuf := make([]byte, 1500)
			n, _, err := conn.ReadFromUDP(readBuf)
			if err != nil {
				return
			}

			packet := &rtp.Packet{}
			if err := packet.Unmarshal(readBuf[:n]); err != nil {
				continue
			}
			s.forwardPacket(packet)
		}
	}()
}

func (b *XeniaBridge) forwardAudioRTP(s *xeniaSession) {
	if s.audioConn == nil {
		return
	}

	for {
		select {
		case <-s.stopped:
			return
		default:
		}

		readBuf := make([]byte, 1500)
		n, _, err := s.audioConn.ReadFromUDP(readBuf)
		if err != nil {
			return
		}

		packet := &rtp.Packet{}
		if err := packet.Unmarshal(readBuf[:n]); err != nil {
			continue
		}

		s.mu.Lock()
		rtc := s.rtc
		s.mu.Unlock()
		if rtc == nil {
			continue
		}

		func() {
			defer func() { _ = recover() }()
			select {
			case rtc.AudioChannel <- packet:
			default:
			}
		}()
	}
}

func (s *xeniaSession) forwardPacket(packet *rtp.Packet) {
	s.mu.Lock()
	rtc := s.rtc
	s.mu.Unlock()

	if rtc == nil {
		return
	}

	defer func() {
		_ = recover() // channel may be closed concurrently during teardown
	}()
	select {
	case rtc.ImageChannel <- packet:
	default:
		// Drop frame under backpressure rather than blocking the reader loop.
	}
}

func (b *XeniaBridge) stopSession(s *xeniaSession, reason string) {
	s.stopOnce.Do(func() {
		s.setStatus("stopping")
		close(s.stopped)

		if s.ffmpegCmd != nil && s.ffmpegCmd.Process != nil {
			_ = s.ffmpegCmd.Process.Kill()
		}
		if s.audioCmd != nil && s.audioCmd.Process != nil {
			_ = s.audioCmd.Process.Kill()
		}
		if s.syncInputCmd != nil && s.syncInputCmd.Process != nil {
			_ = s.syncInputCmd.Process.Kill()
		}

		s.mu.Lock()
		rtc := s.rtc
		s.rtc = nil
		s.mu.Unlock()
		if rtc != nil {
			rtc.StopClient()
		}

		if s.udpConn != nil {
			_ = s.udpConn.Close()
		}
		if s.audioConn != nil {
			_ = s.audioConn.Close()
		}

		s.setStatus("stopped")
		log.Printf("[xenia-bridge] session %s stopped (%s)\n", s.sessionID, reason)
	})
}

// ---------------------------------------------------------------------------
// WebRTC signaling
// ---------------------------------------------------------------------------

type signalMessage struct {
	Type string `json:"type"`
	Data string `json:"data"`
}

// handleSignal upgrades to a WebSocket and drives WebRTC offer/answer/ICE
// exchange for the given stream. The server creates the offer immediately on
// connect (matching the existing cloud-morph webrtc.go client flow).
func (b *XeniaBridge) handleSignal(w http.ResponseWriter, r *http.Request) {
	streamID := mux.Vars(r)["id"]

	b.mu.Lock()
	session := b.byStream[streamID]
	b.mu.Unlock()

	if session == nil {
		http.Error(w, "unknown stream", http.StatusNotFound)
		return
	}

	conn, err := b.upgrader.Upgrade(w, r, nil)
	if err != nil {
		log.Println("[xenia-bridge] signaling upgrade failed:", err)
		return
	}
	defer conn.Close()

	var writeMu sync.Mutex

	// Signaling keepalive: some relayed/tunneled network paths (e.g. Radmin
	// VPN) silently drop idle WebSocket connections faster than the default
	// OS TCP keepalive would notice, which previously caused ICE candidate
	// exchange to die right after the SDP answer with no error logged at
	// all (conn.ReadJSON just returned an error and the loop broke silently).
	// Ping every 15s and require a pong within 20s of the last ping.
	const pongWait = 20 * time.Second
	const pingInterval = 15 * time.Second
	_ = conn.SetReadDeadline(time.Now().Add(pongWait))
	conn.SetPongHandler(func(string) error {
		_ = conn.SetReadDeadline(time.Now().Add(pongWait))
		return nil
	})
	pingStop := make(chan struct{})
	defer close(pingStop)
	go func() {
		ticker := time.NewTicker(pingInterval)
		defer ticker.Stop()
		for {
			select {
			case <-ticker.C:
				writeMu.Lock()
				err := conn.WriteControl(websocket.PingMessage, nil, time.Now().Add(5*time.Second))
				writeMu.Unlock()
				if err != nil {
					return
				}
			case <-pingStop:
				return
			}
		}
	}()

	rtc := crtc.NewWebRTC()
	session.mu.Lock()
	session.rtc = rtc
	session.mu.Unlock()
	session.setControllerStatus("connecting")

	send := func(msg signalMessage) {
		writeMu.Lock()
		defer writeMu.Unlock()
		_ = conn.WriteJSON(msg)
	}

	offer, err := rtc.StartClient(func(candidate string) {
		if candidate == "" {
			return
		}
		send(signalMessage{Type: "candidate", Data: candidate})
	}, &b.rtcConfig)
	if err != nil {
		log.Println("[xenia-bridge] failed to create WebRTC offer:", err)
		return
	}
	send(signalMessage{Type: "offer", Data: offer})

	// Bridge WebRTC data-channel input (JSON control messages) to the emulator.
	go b.pumpInput(session, rtc)

	for {
		var msg signalMessage
		if err := conn.ReadJSON(&msg); err != nil {
			log.Println("[xenia-bridge] signaling read closed:", err)
			break
		}

		switch msg.Type {
		case "answer":
			if err := rtc.SetRemoteSDP(msg.Data); err != nil {
				log.Println("[xenia-bridge] failed to set remote SDP:", err)
			} else {
				session.setControllerStatus("game")
			}
		case "candidate":
			if err := rtc.AddCandidate(msg.Data); err != nil {
				log.Println("[xenia-bridge] failed to add ICE candidate:", err)
			}
		}
	}

	session.setControllerStatus("offline")
}

// controlMessage is the JSON contract sent by the frontend over the
// "app-input" WebRTC data channel. Only "gamepad" is accepted; key/mouse
// payloads are intentionally ignored for stream security.
type controlMessage struct {
	V         int    `json:"v"`
	SessionID string `json:"sessionId"`
	PlayerSlot int   `json:"playerSlot"`
	FrameID   uint64 `json:"frameId"`
	TimestampMs int64 `json:"timestampMs"`
	Type string `json:"type"` // "key" | "mouseMove" | "mouseButton" | "gamepad"

	// key
	Code int  `json:"code"`
	Down bool `json:"down"`

	// mouse
	IsLeft bool    `json:"isLeft"`
	X      float32 `json:"x"`
	Y      float32 `json:"y"`
	Width  float32 `json:"width"`
	Height float32 `json:"height"`

	// gamepad (standard mapping button indexes that are currently pressed,
	// plus digital left-stick direction derived client-side after deadzone).
	Buttons []int  `json:"buttons"`
	Stick   string `json:"stick"` // "up"|"down"|"left"|"right"|""
}

type slotInputState struct {
	pressed map[int]bool
	stick   string
	updated time.Time
}

func (b *XeniaBridge) pumpInput(session *xeniaSession, rtc *crtc.WebRTC) {
	var injector inputInjector = b.syncInput
	appliedPressed := map[int]bool{}
	appliedStick := ""
	slotStates := map[int]*slotInputState{}
	lastInputAt := time.Now()
	releaseTicker := time.NewTicker(200 * time.Millisecond)
	defer releaseTicker.Stop()

	defer func() {
		// Safety: always release any held keys when the input channel closes
		// so the host keyboard state is never left stuck.
		releaseAllInput(injector, appliedPressed, appliedStick)
		_ = recover()
	}()

	for {
		select {
		case raw, ok := <-rtc.InputChannel:
			if !ok {
				return
			}

			lastInputAt = time.Now()

			var msg controlMessage
			if err := json.Unmarshal(raw, &msg); err != nil {
				continue
			}

			switch msg.Type {
			case "gamepad":
				slot := normalizePlayerSlot(msg.PlayerSlot)
				state, ok := slotStates[slot]
				if !ok {
					state = &slotInputState{pressed: map[int]bool{}}
					slotStates[slot] = state
				}

				nextPressed := map[int]bool{}
				for _, idx := range msg.Buttons {
					nextPressed[idx] = true
				}

				state.pressed = nextPressed
				state.stick = normalizeStick(msg.Stick)
				state.updated = time.Now()

				compositeButtons, compositeStick := composeSlots(slotStates)
				b.applyGamepadState(injector, appliedPressed, compositeButtons)
				if compositeStick != appliedStick {
					releaseStick(injector, appliedStick)
					pressStick(injector, compositeStick)
					appliedStick = compositeStick
				}
			default:
				// Intentionally ignore non-controller input types.
			}
		case <-releaseTicker.C:
			if time.Since(lastInputAt) < 600*time.Millisecond {
				continue
			}

			changed := false
			now := time.Now()
			for slot, state := range slotStates {
				if now.Sub(state.updated) > 600*time.Millisecond {
					delete(slotStates, slot)
					changed = true
				}
			}

			if !changed && !hasActiveInput(appliedPressed, appliedStick) {
				continue
			}

			compositeButtons, compositeStick := composeSlots(slotStates)
			b.applyGamepadState(injector, appliedPressed, compositeButtons)
			if compositeStick != appliedStick {
				releaseStick(injector, appliedStick)
				pressStick(injector, compositeStick)
				appliedStick = compositeStick
			}
		}
	}
}

func normalizePlayerSlot(slot int) int {
	if slot < 1 {
		return 1
	}
	if slot > 4 {
		return 4
	}
	return slot
}

func normalizeStick(stick string) string {
	switch stick {
	case "up", "down", "left", "right":
		return stick
	default:
		return ""
	}
}

func composeSlots(slotStates map[int]*slotInputState) ([]int, string) {
	merged := map[int]bool{}

	for slot := 1; slot <= 4; slot++ {
		state, ok := slotStates[slot]
		if !ok {
			continue
		}

		for idx, down := range state.pressed {
			if down {
				merged[idx] = true
			}
		}
	}

	buttons := make([]int, 0, len(merged))
	for idx := range merged {
		buttons = append(buttons, idx)
	}
	sort.Ints(buttons)

	for slot := 1; slot <= 4; slot++ {
		state, ok := slotStates[slot]
		if !ok {
			continue
		}

		if state.stick != "" {
			return buttons, state.stick
		}
	}

	return buttons, ""
}

func hasActiveInput(pressed map[int]bool, lastStick string) bool {
	if lastStick != "" {
		return true
	}

	for _, down := range pressed {
		if down {
			return true
		}
	}

	return false
}

func releaseAllInput(injector inputInjector, pressed map[int]bool, lastStick string) {
	for idx, wasDown := range pressed {
		if !wasDown {
			continue
		}

		if codes, ok := gamepadKeyMap[idx]; ok {
			for i := len(codes) - 1; i >= 0; i-- {
				injector.SendKey(codes[i], false)
			}
		}
	}

	releaseStick(injector, lastStick)
}

func (b *XeniaBridge) applyGamepadState(injector inputInjector, pressed map[int]bool, buttons []int) {
	nowPressed := map[int]bool{}
	for _, idx := range buttons {
		nowPressed[idx] = true
	}

	for idx, codes := range gamepadKeyMap {
		wasDown := pressed[idx]
		isDown := nowPressed[idx]
		if isDown != wasDown {
			if isDown {
				for _, code := range codes {
					injector.SendKey(code, true)
				}
			} else {
				for i := len(codes) - 1; i >= 0; i-- {
					injector.SendKey(codes[i], false)
				}
			}
			pressed[idx] = isDown
		}
	}
}

func pressStick(injector inputInjector, direction string) {
	if codes, ok := gamepadAxisKeyMap[direction]; ok {
		for _, code := range codes {
			injector.SendKey(code, true)
		}
	}
}

func releaseStick(injector inputInjector, direction string) {
	if codes, ok := gamepadAxisKeyMap[direction]; ok {
		for i := len(codes) - 1; i >= 0; i-- {
			injector.SendKey(codes[i], false)
		}
	}
}
