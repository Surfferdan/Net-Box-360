import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import net from "node:net";
import http from "node:http";
import { fileURLToPath } from "node:url";
import path from "node:path";

const thisFile = fileURLToPath(import.meta.url);
const webPortRoot = path.dirname(path.dirname(thisFile));
const repoRoot = path.resolve(webPortRoot, "..");
const devBuildRoot = path.join(webPortRoot, ".dev-build");
const apiRoot = path.join(repoRoot, "xenia api", "XeniaManager.Api");
const apiBuildOutput = path.join(devBuildRoot, "api");
const apiDllPath = path.join(apiBuildOutput, "XeniaManager.Api.dll");
const apiExePath = path.join(apiBuildOutput, "XeniaManager.Api.exe");
const cloudMorphRoot = path.join(repoRoot, "cloud morph code", "cloud-morph-master");
const cloudMorphBuildOutput = path.join(devBuildRoot, "cloudmorph");
const cloudMorphExePath = path.join(
  cloudMorphBuildOutput,
  process.platform === "win32" ? "cloudmorph-dev.exe" : "cloudmorph-dev",
);

let shuttingDown = false;
let apiProcess;
let webProcess;
let cloudMorphProcess;
let resolveKeepAlive;
let apiPort = 5077;
let apiReady = false;
let cloudMorphReady = false;
let apiRestartTimer;
let useExistingApi = false;
let useExistingCloudMorph = false;

const defaultApiBaseUrl = "http://127.0.0.1:5077";

const keepAlive = new Promise((resolve) => {
  resolveKeepAlive = resolve;
});

async function findFreePort(preferredPort) {
  const freePort = await new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.on("error", reject);
    server.listen(preferredPort, "127.0.0.1", () => {
      const address = server.address();
      const resolvedPort = typeof address === "object" && address ? address.port : null;
      server.close(() => resolve(resolvedPort));
    });
  }).catch(() => null);

  if (freePort) {
    return freePort;
  }

  throw new Error(`Unable to bind API to fixed port ${preferredPort}. Stop other dev sessions and retry.`);
}

function spawnProcess(command, args, options) {
  const child = spawn(command, args, {
    stdio: "inherit",
    shell: false,
    ...options,
  });

  child.on("error", (error) => {
    console.error(`[dev] ${command} spawn failed:`, error);
  });

  return child;
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function isPortOpen(port) {
  return new Promise((resolve) => {
    const socket = net.createConnection({ host: "127.0.0.1", port });
    socket.once("connect", () => {
      socket.destroy();
      resolve(true);
    });
    socket.once("error", () => {
      socket.destroy();
      resolve(false);
    });
  });
}

async function waitForApiPort(port, timeoutMs = 60000) {
  const started = Date.now();

  while (Date.now() - started < timeoutMs) {
    const isOpen = await isPortOpen(port);

    if (isOpen) {
      return;
    }

    await delay(250);
  }

  throw new Error(`API did not start listening on http://127.0.0.1:${port} within ${timeoutMs}ms.`);
}

async function waitForCloudMorphHealth(timeoutMs = 180000) {
  const started = Date.now();
  let attempt = 0;

  while (Date.now() - started < timeoutMs) {
    attempt += 1;
    const isHealthy = await new Promise((resolve) => {
      const req = http.get("http://127.0.0.1:8080/healthz", (res) => {
        let body = "";
        res.setEncoding("utf8");
        res.on("data", (chunk) => {
          body += chunk;
        });
        res.on("end", () => {
          resolve(res.statusCode === 200);
          if (res.statusCode === 200) {
            console.log(`[dev] CloudMorph health probe ${attempt} succeeded: ${body.trim()}`);
          }
        });
      });
      req.on("error", () => resolve(false));
      req.setTimeout(2500, () => {
        req.destroy();
        resolve(false);
      });
    });

    if (isHealthy) {
      return;
    }

    if (attempt % 8 === 0) {
      console.log(`[dev] CloudMorph still warming up... (${Math.round((Date.now() - started) / 1000)}s)`);
    }

    await delay(1000);
  }

  throw new Error(`CloudMorph did not become healthy on http://127.0.0.1:8080/healthz within ${timeoutMs}ms.`);
}

async function waitForApiHealth(baseUrl, timeoutMs = 120000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (await hasHealthyApi(baseUrl, 2500)) {
      return;
    }

    await delay(1000);
  }

  throw new Error(`API did not become healthy at ${baseUrl}/api/diagnostics within ${timeoutMs}ms.`);
}

async function hasHealthyApi(baseUrl, timeoutMs = 2500) {
  return new Promise((resolve) => {
    const req = http.get(`${baseUrl}/api/diagnostics`, (res) => {
      res.resume();
      resolve(res.statusCode === 200);
    });
    req.on("error", () => resolve(false));
    req.setTimeout(timeoutMs, () => {
      req.destroy();
      resolve(false);
    });
  });
}

function killProcessTree(child) {
  if (!child?.pid) {
    return;
  }

  if (process.platform === "win32") {
    spawnSync("taskkill", ["/pid", String(child.pid), "/t", "/f"], { stdio: "ignore" });
    return;
  }

  child.kill("SIGTERM");
}

function killStaleApiExecutables() {
  if (process.platform !== "win32") {
    return;
  }

  spawnSync("taskkill", ["/im", "XeniaManager.Api.exe", "/t", "/f"], { stdio: "ignore" });
}

function killStaleCloudMorphProcesses() {
  if (process.platform !== "win32") {
    return;
  }

  // Kill any leftover CloudMorph/Xenia bridge dev binary and its per-session
  // child processes (ffmpeg capture + syncinput.exe input relay) so a
  // rebuild never hits a file-in-use error and a restart never leaves a
  // stale capture pipeline holding the RTP port or window handle.
  spawnSync("taskkill", ["/im", "cloudmorph-dev.exe", "/t", "/f"], { stdio: "ignore" });
  spawnSync("taskkill", ["/im", "syncinput.exe", "/t", "/f"], { stdio: "ignore" });
}

function releaseListenerPortOnWindows(port) {
  if (process.platform !== "win32") {
    return;
  }

  const ps = spawnSync(
    "powershell",
    [
      "-NoProfile",
      "-Command",
      `Get-NetTCPConnection -State Listen -LocalPort ${port} -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }`,
    ],
    {
      stdio: "ignore",
      shell: false,
    },
  );

  if ((ps.status ?? 0) === 0) {
    return;
  }

  const netstat = spawnSync("cmd", ["/c", `netstat -ano | findstr /R /C:":${port} .*LISTENING"`], {
    stdio: ["ignore", "pipe", "ignore"],
    encoding: "utf8",
    shell: false,
  });

  const output = (netstat.stdout || "").trim();
  if (!output) {
    return;
  }

  const pids = new Set(
    output
      .split(/\r?\n/)
      .map((line) => line.trim().split(/\s+/).at(-1))
      .filter(Boolean),
  );

  for (const pid of pids) {
    spawnSync("taskkill", ["/pid", String(pid), "/t", "/f"], { stdio: "ignore", shell: false });
  }
}

async function ensureVitePortAvailable(port = 3600) {
  if (!(await isPortOpen(port))) {
    return;
  }

  if (process.platform === "win32") {
    console.warn(`[dev] Port ${port} already in use. Attempting to release it...`);
    releaseListenerPortOnWindows(port);
    await delay(750);
  }

  if (await isPortOpen(port)) {
    throw new Error(`Port ${port} is already in use. Stop the existing Vite server and retry.`);
  }
}

async function launchAdminApiIfNeeded() {
  if (process.platform !== "win32") {
    return false;
  }

  const scriptPath = path.join(repoRoot, "start-api-admin.bat");
  if (!fs.existsSync(scriptPath)) {
    return false;
  }

  console.log("[dev] API is down on 5077. Requesting elevated API launch...");
  const result = spawnSync("cmd", ["/c", scriptPath], {
    cwd: repoRoot,
    stdio: "inherit",
    shell: false,
  });

  if ((result.status ?? 1) !== 0) {
    throw new Error("Failed to launch start-api-admin.bat.");
  }

  console.log("[dev] Waiting for elevated API to come online on 5077...");
  await waitForApiHealth(defaultApiBaseUrl, 120000);
  return true;
}

function ensureBuildDirectories() {
  fs.mkdirSync(apiBuildOutput, { recursive: true });
  fs.mkdirSync(cloudMorphBuildOutput, { recursive: true });
}

function buildApiProject() {
  ensureBuildDirectories();
  const result = spawnSync("dotnet", ["build", "--output", apiBuildOutput], {
    cwd: apiRoot,
    stdio: "inherit",
    shell: false,
  });

  if ((result.status ?? 1) !== 0) {
    throw new Error("API build failed. Resolve build errors and retry.");
  }
}

function buildCloudMorphProject(goExe) {
  ensureBuildDirectories();
  // Build the whole `main` package (not just server.go) so xenia_bridge.go
  // and any other package-main files are included. Building server.go alone
  // causes "undefined: XeniaBridge" style errors since Go only compiles the
  // explicitly listed files in that mode.
  const result = spawnSync(goExe, ["build", "-o", cloudMorphExePath, "."], {
    cwd: cloudMorphRoot,
    stdio: "inherit",
    shell: false,
    env: {
      ...process.env,
      GO111MODULE: "on",
    },
  });

  if ((result.status ?? 1) !== 0) {
    throw new Error("CloudMorph build failed. Resolve Go build errors and retry.");
  }
}

async function releaseApiPort(port, attempts = 12) {
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    if (!(await isPortOpen(port))) {
      return;
    }

    killStaleApiExecutables();
    await delay(250);
  }
}

function shutdown(exitCode = 0) {
  if (shuttingDown) {
    return;
  }

  shuttingDown = true;
  if (apiRestartTimer) {
    clearTimeout(apiRestartTimer);
    apiRestartTimer = undefined;
  }
  if (!useExistingApi) {
    killStaleApiExecutables();
  }
  if (!useExistingCloudMorph) {
    killStaleCloudMorphProcesses();
  }
  killProcessTree(apiProcess);
  killProcessTree(cloudMorphProcess);
  killProcessTree(webProcess);
  resolveKeepAlive?.();
  process.exitCode = exitCode;
}

process.on("SIGINT", () => shutdown(0));
process.on("SIGTERM", () => shutdown(0));
process.on("exit", () => shutdown(0));

function resolveGoExecutable() {
  const candidates = [];

  if (process.env.GO_EXE) {
    candidates.push(process.env.GO_EXE);
  }

  if (process.env.GOROOT) {
    candidates.push(path.join(process.env.GOROOT, "bin", process.platform === "win32" ? "go.exe" : "go"));
  }

  if (process.platform === "win32") {
    const programFiles = process.env.ProgramFiles || "C:\\Program Files";
    candidates.push(path.join(programFiles, "Go", "bin", "go.exe"));
  } else {
    candidates.push("go");
  }

  for (const candidate of candidates) {
    if (!candidate) {
      continue;
    }

    if (candidate === "go") {
      try {
        const result = spawnSync(candidate, ["version"], { stdio: "ignore", shell: false });
        if ((result.status ?? 1) === 0) {
          return candidate;
        }
      } catch {
        // try next candidate
      }
      continue;
    }

    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }

  return null;
}

function startCloudMorphProcess() {
  if (!fs.existsSync(cloudMorphExePath)) {
    throw new Error("CloudMorph executable not found. Build should have produced cloudmorph-dev binary.");
  }

  cloudMorphProcess = spawnProcess(cloudMorphExePath, [], {
    cwd: cloudMorphRoot,
    env: {
      ...process.env,
      GO111MODULE: "on",
    },
  });
  console.log(`[dev] CloudMorph pid ${cloudMorphProcess.pid} cwd ${cloudMorphRoot}`);

  cloudMorphProcess.on("exit", async (code, signal) => {
    if (shuttingDown) {
      return;
    }

    cloudMorphReady = false;
    console.error(`[dev] CloudMorph exited unexpectedly (${code ?? signal ?? 0}). Restarting CloudMorph...`);
    // The Go process is not the direct parent of a job object, so its ffmpeg
    // capture / syncinput.exe input-relay children can outlive it; clear
    // them before restarting so the next session starts from a clean slate.
    killStaleCloudMorphProcesses();
    setTimeout(() => {
      if (!shuttingDown) {
        startCloudMorphProcess();
      }
    }, 1000);
  });
}

function startApiProcess() {
  apiReady = false;
  const launchCommand = process.platform === "win32" ? apiExePath : "dotnet";
  const launchArgs = process.platform === "win32"
    ? ["--urls", `http://127.0.0.1:${apiPort}`]
    : [apiDllPath, "--urls", `http://127.0.0.1:${apiPort}`];

  apiProcess = spawnProcess(launchCommand, launchArgs, {
    cwd: apiRoot,
    env: {
      ...process.env,
      XeniaApi__BaseUrl: apiBaseUrl,
    },
  });
  console.log(`[dev] API pid ${apiProcess.pid} cwd ${apiRoot}`);

  apiProcess.on("exit", async (code, signal) => {
    if (shuttingDown) {
      return;
    }

    apiReady = false;
    await delay(250);
    const apiStillReachable = await isPortOpen(apiPort);
    if (apiStillReachable) {
      console.warn(`[dev] API process exited (${code ?? signal ?? 0}) but port ${apiPort} is still serving.`);
      return;
    }

    console.error(`[dev] API exited unexpectedly (${code ?? signal ?? 0}). Restarting API only...`);
    if (apiRestartTimer) {
      clearTimeout(apiRestartTimer);
    }

    apiRestartTimer = setTimeout(() => {
      if (!shuttingDown && (!webProcess || webProcess.exitCode === null)) {
        startApiProcess();
      }
    }, 1000);
  });
}

const apiAlreadyListening = await isPortOpen(5077);
if (apiAlreadyListening && await hasHealthyApi(defaultApiBaseUrl)) {
  useExistingApi = true;
  apiPort = 5077;
  console.log("[dev] Reusing existing API at http://127.0.0.1:5077");
} else if (apiAlreadyListening) {
  console.warn("[dev] Port 5077 is occupied but API health check failed. Attempting cleanup...");
  if (process.platform === "win32") {
    killStaleApiExecutables();
    await releaseApiPort(5077);
  }
}

if (!useExistingApi) {
  if (await hasHealthyApi(defaultApiBaseUrl)) {
    useExistingApi = true;
    apiPort = 5077;
    console.log("[dev] Reusing existing API at http://127.0.0.1:5077");
  } else if (await launchAdminApiIfNeeded()) {
    useExistingApi = true;
    apiPort = 5077;
    console.log("[dev] Elevated API ready at http://127.0.0.1:5077");
  }
}

const cloudMorphAlreadyHealthy = await isPortOpen(8080) && await waitForCloudMorphHealth(3000).then(() => true).catch(() => false);
if (cloudMorphAlreadyHealthy) {
  useExistingCloudMorph = true;
  console.log("[dev] Reusing existing CloudMorph at http://127.0.0.1:8080");
}

if (!useExistingApi) {
  killStaleApiExecutables();
  await releaseApiPort(5077);
  apiPort = await findFreePort(5077);
  console.log(`[dev] Using API port ${apiPort}`);
}

if (!useExistingCloudMorph) {
  killStaleCloudMorphProcesses();
}

const apiBaseUrl = `http://127.0.0.1:${apiPort}`;
if (!useExistingApi) {
  await delay(5000);
  killStaleApiExecutables();
  await delay(2000);
}

const needsGoToolchain = !useExistingApi || !useExistingCloudMorph;
const goExe = needsGoToolchain ? resolveGoExecutable() : null;
if (needsGoToolchain && !goExe) {
  throw new Error("Unable to locate the Go executable for CloudMorph. Install Go or add it to PATH.");
}
if (!useExistingApi) {
  buildApiProject();
}
if (!useExistingCloudMorph) {
  buildCloudMorphProject(goExe);
}
if (!useExistingApi) {
  await releaseApiPort(apiPort);
}
if (!useExistingCloudMorph) {
  startCloudMorphProcess();
}
if (!useExistingApi) {
  startApiProcess();
}

if (useExistingApi) {
  apiReady = true;
  console.log(`[dev] API ready at ${apiBaseUrl}`);
} else {
  try {
    await waitForApiPort(apiPort);
    apiReady = true;
    console.log(`[dev] API ready at ${apiBaseUrl}`);
  } catch (error) {
    console.error("[dev] API failed to become ready:", error);
    shutdown();
    throw error;
  }
}

if (useExistingCloudMorph) {
  cloudMorphReady = true;
  console.log("[dev] CloudMorph ready at http://127.0.0.1:8080/healthz");
} else {
  try {
    await waitForCloudMorphHealth();
    cloudMorphReady = true;
    console.log("[dev] CloudMorph ready at http://127.0.0.1:8080/healthz");
  } catch (error) {
    console.error("[dev] CloudMorph failed to become ready:", error);
    shutdown();
    throw error;
  }
}

await ensureVitePortAvailable(3600);

webProcess = spawnProcess(process.execPath, [
  path.join(webPortRoot, "node_modules", "vite", "bin", "vite.js"),
  "--host",
  "0.0.0.0",
  "--port",
  "3600",
  "--strictPort",
], {
  cwd: webPortRoot,
  env: {
    ...process.env,
    VITE_NETBOX_API_BASE_URL: "",
    VITE_DEV_API_TARGET: apiBaseUrl,
  },
});
console.log(`[dev] Vite pid ${webProcess.pid} cwd ${webPortRoot}`);

webProcess.on("exit", (code, signal) => {
  if (shuttingDown) {
    return;
  }

  console.error(`[dev] Vite exited (${code ?? signal ?? 0}).`);
  console.error(`[dev] Vite exited unexpectedly (${code ?? signal ?? 0}). Shutting down dev stack.`);
  shutdown();
  process.exitCode = code ?? 1;
});

await keepAlive;

