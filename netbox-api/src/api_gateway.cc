#include "netbox_api/api_gateway.h"

#include <sstream>
#include <vector>

#include "netbox_api/json.h"

namespace netbox_api {

namespace {

std::vector<std::string> SplitPath(const std::string& path) {
  std::vector<std::string> parts;
  std::stringstream ss(path);
  std::string part;
  while (std::getline(ss, part, '/')) {
    if (!part.empty()) {
      parts.push_back(part);
    }
  }
  return parts;
}

bool ParseId(const std::string& text, uint64_t* out) {
  if (text.empty()) {
    return false;
  }
  try {
    size_t consumed = 0;
    unsigned long long value = std::stoull(text, &consumed);
    if (consumed != text.size()) {
      return false;
    }
    *out = value;
    return true;
  } catch (...) {
    return false;
  }
}

// Extracts the value of a "controller_slot":N field from a flat JSON body.
// Deliberately minimal (no general JSON parsing dependency) - matches the
// one request body shape this API actually needs to read.
bool ParseControllerSlot(const std::string& body, uint32_t* out) {
  const std::string key = "\"controller_slot\"";
  size_t pos = body.find(key);
  if (pos == std::string::npos) {
    return false;
  }
  pos = body.find(':', pos + key.size());
  if (pos == std::string::npos) {
    return false;
  }
  ++pos;
  while (pos < body.size() && std::isspace(static_cast<unsigned char>(body[pos]))) {
    ++pos;
  }
  size_t start = pos;
  while (pos < body.size() && std::isdigit(static_cast<unsigned char>(body[pos]))) {
    ++pos;
  }
  if (pos == start) {
    return false;
  }
  *out = static_cast<uint32_t>(std::stoul(body.substr(start, pos - start)));
  return true;
}

std::string SessionToJson(const netbox_server::Session& session) {
  netbox_api::JsonObject obj;
  obj.Set("id", static_cast<long long>(session.id));
  obj.Set("runtime", static_cast<long long>(session.runtime));
  obj.Set("stream", static_cast<long long>(session.stream));
  obj.Set("state", netbox_server::SessionStateToString(session.state));
  netbox_api::JsonArray players;
  for (auto player : session.players) {
    players.Add(std::to_string(player));
  }
  obj.Set("players", netbox_api::JsonValue::Raw(players.ToString()));
  return obj.ToString();
}

std::string PlayerToJson(const netbox_server::PlayerInfo& info) {
  netbox_api::JsonObject obj;
  obj.Set("id", static_cast<long long>(info.id));
  obj.Set("session", static_cast<long long>(info.session));
  obj.Set("controller_slot", static_cast<long long>(info.controller_slot));
  obj.Set("connection_state",
         netbox_server::PlayerConnectionStateToString(info.connection_state));
  return obj.ToString();
}

// Maps stream/runtime health into the API's simplified "state" vocabulary
// from the spec's example response ("running"/"stopped"/"failed").
const char* StreamStateString(netbox_server::HealthState health) {
  switch (health) {
    case netbox_server::HealthState::kHealthy:
      return "running";
    case netbox_server::HealthState::kFailed:
      return "failed";
    case netbox_server::HealthState::kDegraded:
      return "degraded";
    case netbox_server::HealthState::kUnknown:
    default:
      return "stopped";
  }
}

}  // namespace

ApiGateway::ApiGateway(netbox_server::SessionManager& session_manager,
                      netbox_server::RuntimeFactory runtime_factory,
                      netbox_server::StreamFactory stream_factory,
                      std::string stream_backend_name)
    : session_manager_(session_manager),
      runtime_factory_(std::move(runtime_factory)),
      stream_factory_(std::move(stream_factory)),
      stream_backend_name_(std::move(stream_backend_name)) {}

HttpResponse ApiGateway::HandleRequest(const HttpRequest& request) {
  std::vector<std::string> parts = SplitPath(request.path);

  // /sessions
  if (parts.size() == 1 && parts[0] == "sessions") {
    if (request.method == HttpMethod::kPost) return CreateSession();
    if (request.method == HttpMethod::kGet) return ListSessions();
    return HttpResponse::BadRequest("unsupported_method");
  }

  if (parts.size() >= 2 && parts[0] == "sessions") {
    uint64_t session_id_raw;
    if (!ParseId(parts[1], &session_id_raw)) {
      return HttpResponse::BadRequest("invalid_session_id");
    }
    auto session_id = static_cast<netbox_server::SessionId>(session_id_raw);

    // /sessions/{id}
    if (parts.size() == 2) {
      if (request.method == HttpMethod::kGet) return GetSession(session_id);
      if (request.method == HttpMethod::kDelete) return DestroySession(session_id);
      return HttpResponse::BadRequest("unsupported_method");
    }

    // /sessions/{id}/start | /stop | /players | /stream
    if (parts.size() == 3) {
      if (parts[2] == "start" && request.method == HttpMethod::kPost) {
        return StartSession(session_id);
      }
      if (parts[2] == "stop" && request.method == HttpMethod::kPost) {
        return StopSession(session_id);
      }
      if (parts[2] == "players") {
        if (request.method == HttpMethod::kPost) {
          return JoinPlayer(session_id, request.body);
        }
        if (request.method == HttpMethod::kGet) {
          return ListPlayers(session_id);
        }
        return HttpResponse::BadRequest("unsupported_method");
      }
      if (parts[2] == "stream" && request.method == HttpMethod::kGet) {
        return GetStream(session_id);
      }
      return HttpResponse::NotFound();
    }

    // /sessions/{id}/players/{player}
    if (parts.size() == 4 && parts[2] == "players" &&
        request.method == HttpMethod::kDelete) {
      uint64_t player_id_raw;
      if (!ParseId(parts[3], &player_id_raw)) {
        return HttpResponse::BadRequest("invalid_player_id");
      }
      return LeavePlayer(session_id,
                        static_cast<netbox_server::PlayerId>(player_id_raw));
    }
  }

  return HttpResponse::NotFound();
}

HttpResponse ApiGateway::CreateSession() {
  netbox_server::SessionId id = session_manager_.CreateSession(
      runtime_factory_, stream_factory_, stream_backend_name_);
  if (id == netbox_server::kInvalidSessionId) {
    return HttpResponse::BadRequest("session_creation_failed");
  }
  netbox_server::Session session = session_manager_.GetSession(id);
  return HttpResponse::Ok(SessionToJson(session));
}

HttpResponse ApiGateway::ListSessions() {
  netbox_api::JsonArray array;
  for (const auto& session : session_manager_.ListSessions()) {
    array.Add(SessionToJson(session));
  }
  return HttpResponse::Ok(array.ToString());
}

HttpResponse ApiGateway::GetSession(netbox_server::SessionId id) {
  netbox_server::Session session = session_manager_.GetSession(id);
  if (session.id == netbox_server::kInvalidSessionId) {
    return HttpResponse::NotFound();
  }
  return HttpResponse::Ok(SessionToJson(session));
}

HttpResponse ApiGateway::StartSession(netbox_server::SessionId id) {
  netbox_server::Session existing = session_manager_.GetSession(id);
  if (existing.id == netbox_server::kInvalidSessionId) {
    return HttpResponse::NotFound();
  }
  bool started = session_manager_.StartSession(id);
  netbox_server::Session session = session_manager_.GetSession(id);
  if (!started) {
    return HttpResponse::Conflict("session_failed_to_start");
  }
  return HttpResponse::Ok(SessionToJson(session));
}

HttpResponse ApiGateway::StopSession(netbox_server::SessionId id) {
  netbox_server::Session existing = session_manager_.GetSession(id);
  if (existing.id == netbox_server::kInvalidSessionId) {
    return HttpResponse::NotFound();
  }
  session_manager_.StopSession(id);
  netbox_server::Session session = session_manager_.GetSession(id);
  return HttpResponse::Ok(SessionToJson(session));
}

HttpResponse ApiGateway::DestroySession(netbox_server::SessionId id) {
  netbox_server::Session existing = session_manager_.GetSession(id);
  if (existing.id == netbox_server::kInvalidSessionId) {
    return HttpResponse::NotFound();
  }
  session_manager_.DestroySession(id);
  return HttpResponse{204, ""};
}

HttpResponse ApiGateway::JoinPlayer(netbox_server::SessionId id,
                                    const std::string& body) {
  netbox_server::Session existing = session_manager_.GetSession(id);
  if (existing.id == netbox_server::kInvalidSessionId) {
    return HttpResponse::NotFound();
  }
  uint32_t controller_slot = 0;
  if (!ParseControllerSlot(body, &controller_slot)) {
    return HttpResponse::BadRequest("missing_controller_slot");
  }
  netbox_server::PlayerId player_id =
      session_manager_.AssignPlayer(id, controller_slot);
  if (player_id == netbox_server::kInvalidPlayerId) {
    return HttpResponse::Conflict("controller_slot_occupied");
  }
  netbox_server::PlayerInfo info =
      session_manager_.player_registry().GetInfo(player_id);
  return HttpResponse::Ok(PlayerToJson(info));
}

HttpResponse ApiGateway::LeavePlayer(netbox_server::SessionId id,
                                    netbox_server::PlayerId player) {
  netbox_server::Session existing = session_manager_.GetSession(id);
  if (existing.id == netbox_server::kInvalidSessionId) {
    return HttpResponse::NotFound();
  }
  if (!session_manager_.ReleasePlayer(id, player)) {
    return HttpResponse::NotFound();
  }
  return HttpResponse{204, ""};
}

HttpResponse ApiGateway::ListPlayers(netbox_server::SessionId id) {
  netbox_server::Session existing = session_manager_.GetSession(id);
  if (existing.id == netbox_server::kInvalidSessionId) {
    return HttpResponse::NotFound();
  }
  netbox_api::JsonArray array;
  for (const auto& info :
      session_manager_.player_registry().ListPlayersForSession(id)) {
    array.Add(PlayerToJson(info));
  }
  return HttpResponse::Ok(array.ToString());
}

HttpResponse ApiGateway::GetStream(netbox_server::SessionId id) {
  netbox_server::Session session = session_manager_.GetSession(id);
  if (session.id == netbox_server::kInvalidSessionId) {
    return HttpResponse::NotFound();
  }
  netbox_server::StreamInfo info =
      session_manager_.stream_registry().GetInfo(session.stream);
  netbox_api::JsonObject obj;
  obj.Set("state", StreamStateString(info.health));
  obj.Set("connection", "webrtc");
  return HttpResponse::Ok(obj.ToString());
}

}  // namespace netbox_api
