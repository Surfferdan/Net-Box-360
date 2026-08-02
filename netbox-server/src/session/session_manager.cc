#include "netbox_server/session/session_manager.h"

#include <algorithm>

namespace netbox_server {

SessionId SessionManager::CreateSession(const RuntimeFactory& runtime_factory,
                                        const StreamFactory& stream_factory,
                                        const std::string& stream_backend_name) {
  RuntimeId runtime_id = runtime_registry_.CreateRuntime(runtime_factory);
  if (runtime_id == kInvalidRuntimeId) {
    return kInvalidSessionId;
  }

  StreamId stream_id =
      stream_registry_.CreateStream(stream_factory, stream_backend_name);
  if (stream_id == kInvalidStreamId) {
    runtime_registry_.RemoveRuntime(runtime_id);
    return kInvalidSessionId;
  }

  runtime_registry_.AssignStream(runtime_id, stream_id);

  std::lock_guard<std::mutex> lock(mutex_);
  SessionId id = next_id_++;
  Session session;
  session.id = id;
  session.runtime = runtime_id;
  session.stream = stream_id;
  session.state = SessionState::kCreated;
  sessions_.emplace(id, session);
  return id;
}

bool SessionManager::StartSession(SessionId id) {
  RuntimeId runtime_id;
  StreamId stream_id;
  {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = sessions_.find(id);
    if (it == sessions_.end()) {
      return false;
    }
    if (it->second.state == SessionState::kStarting ||
        it->second.state == SessionState::kRunning) {
      // Idempotent.
      return it->second.state == SessionState::kRunning;
    }
    it->second.state = SessionState::kStarting;
    runtime_id = it->second.runtime;
    stream_id = it->second.stream;
  }

  IRuntimeHandle* runtime_handle = runtime_registry_.GetHandle(runtime_id);
  IStreamHandle* stream_handle = stream_registry_.GetHandle(stream_id);

  bool runtime_started = runtime_handle && runtime_handle->Start();
  if (!runtime_started) {
    // "Failed runtime handling": never start the stream if the runtime
    // itself couldn't come up, and never report Running.
    {
      std::lock_guard<std::mutex> lock(mutex_);
      auto it = sessions_.find(id);
      if (it != sessions_.end()) {
        it->second.state = SessionState::kFailed;
      }
    }
    events_.Dispatch({NetBoxEventType::kRuntimeFailed, id});
    return false;
  }
  events_.Dispatch({NetBoxEventType::kRuntimeStarted, id});

  bool stream_started = stream_handle && stream_handle->Start();
  if (!stream_started) {
    // Roll back the runtime rather than leaving a half-running session.
    runtime_handle->Stop();
    {
      std::lock_guard<std::mutex> lock(mutex_);
      auto it = sessions_.find(id);
      if (it != sessions_.end()) {
        it->second.state = SessionState::kFailed;
      }
    }
    events_.Dispatch({NetBoxEventType::kRuntimeStopped, id});
    events_.Dispatch({NetBoxEventType::kStreamFailed, id});
    return false;
  }
  events_.Dispatch({NetBoxEventType::kStreamHealthy, id});

  std::lock_guard<std::mutex> lock(mutex_);
  auto it = sessions_.find(id);
  if (it != sessions_.end()) {
    it->second.state = SessionState::kRunning;
  }
  return true;
}

bool SessionManager::StopSession(SessionId id) {
  RuntimeId runtime_id;
  StreamId stream_id;
  {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = sessions_.find(id);
    if (it == sessions_.end()) {
      return false;
    }
    if (it->second.state == SessionState::kStopped ||
        it->second.state == SessionState::kCreated) {
      // Nothing to stop.
      it->second.state = SessionState::kStopped;
      return true;
    }
    it->second.state = SessionState::kStopping;
    runtime_id = it->second.runtime;
    stream_id = it->second.stream;
  }

  if (IStreamHandle* stream_handle = stream_registry_.GetHandle(stream_id)) {
    stream_handle->Stop();
  }
  if (IRuntimeHandle* runtime_handle = runtime_registry_.GetHandle(runtime_id)) {
    runtime_handle->Stop();
  }

  {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = sessions_.find(id);
    if (it != sessions_.end()) {
      it->second.state = SessionState::kStopped;
    }
  }
  events_.Dispatch({NetBoxEventType::kRuntimeStopped, id});
  return true;
}

bool SessionManager::DestroySession(SessionId id) {
  RuntimeId runtime_id;
  StreamId stream_id;
  {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = sessions_.find(id);
    if (it == sessions_.end()) {
      return false;
    }
    runtime_id = it->second.runtime;
    stream_id = it->second.stream;
  }

  StopSession(id);

  player_registry_.ReleaseSessionPlayers(id);
  runtime_registry_.RemoveRuntime(runtime_id);
  stream_registry_.RemoveStream(stream_id);

  std::lock_guard<std::mutex> lock(mutex_);
  sessions_.erase(id);
  return true;
}

std::vector<Session> SessionManager::ListSessions() const {
  std::lock_guard<std::mutex> lock(mutex_);
  std::vector<Session> result;
  result.reserve(sessions_.size());
  for (const auto& [id, session] : sessions_) {
    result.push_back(session);
  }
  return result;
}

Session SessionManager::GetSession(SessionId id) const {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = sessions_.find(id);
  if (it == sessions_.end()) {
    return Session{};
  }
  return it->second;
}

PlayerId SessionManager::AssignPlayer(SessionId session,
                                      uint32_t controller_slot) {
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (sessions_.find(session) == sessions_.end()) {
      return kInvalidPlayerId;
    }
  }

  PlayerId player_id = player_registry_.AssignPlayer(session, controller_slot);
  if (player_id == kInvalidPlayerId) {
    return kInvalidPlayerId;
  }

  {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = sessions_.find(session);
    if (it != sessions_.end()) {
      it->second.players.push_back(player_id);
    }
  }
  events_.Dispatch({NetBoxEventType::kPlayerJoined, session, player_id});
  return player_id;
}

bool SessionManager::ReleasePlayer(SessionId session, PlayerId player) {
  {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = sessions_.find(session);
    if (it == sessions_.end()) {
      return false;
    }
    auto& players = it->second.players;
    players.erase(std::remove(players.begin(), players.end(), player),
                 players.end());
  }
  player_registry_.ReleasePlayer(player);
  events_.Dispatch({NetBoxEventType::kPlayerLeft, session, player});
  return true;
}

}  // namespace netbox_server
