#ifndef NETBOX_API_API_GATEWAY_H_
#define NETBOX_API_API_GATEWAY_H_

#include "netbox_api/http_types.h"
#include "netbox_server/runtime/runtime_registry.h"
#include "netbox_server/session/session_manager.h"
#include "netbox_server/stream/stream_registry.h"

namespace netbox_api {

// The control-plane REST boundary in front of a netbox_server::SessionManager.
// Routes:
//   POST   /sessions
//   GET    /sessions
//   GET    /sessions/{id}
//   POST   /sessions/{id}/start
//   POST   /sessions/{id}/stop
//   DELETE /sessions/{id}
//   POST   /sessions/{id}/players
//   DELETE /sessions/{id}/players/{player}
//   GET    /sessions/{id}/players
//   GET    /sessions/{id}/stream
//
// ApiGateway owns no networking - HandleRequest() is a plain synchronous
// function call, so it can be exercised by tests (or wrapped by a real
// HTTP server later) without any socket/framework dependency.
//
// This class implements no authentication, no database, no frontend UI,
// and no matchmaking - purely the API boundary in front of SessionManager,
// per the Phase 9 requirements.
class ApiGateway {
 public:
  // `session_manager` is not owned - caller (e.g. the process wiring
  // together the whole server) retains ownership and lifetime.
  // `runtime_factory`/`stream_factory` are used for POST /sessions, mirroring
  // SessionManager::CreateSession()'s own factory-injection pattern so
  // tests can supply mocks without a real Xenia process or CloudMorph
  // backend.
  ApiGateway(netbox_server::SessionManager& session_manager,
            netbox_server::RuntimeFactory runtime_factory,
            netbox_server::StreamFactory stream_factory,
            std::string stream_backend_name = "cloudmorph");

  HttpResponse HandleRequest(const HttpRequest& request);

 private:
  HttpResponse CreateSession();
  HttpResponse ListSessions();
  HttpResponse GetSession(netbox_server::SessionId id);
  HttpResponse StartSession(netbox_server::SessionId id);
  HttpResponse StopSession(netbox_server::SessionId id);
  HttpResponse DestroySession(netbox_server::SessionId id);
  HttpResponse JoinPlayer(netbox_server::SessionId id, const std::string& body);
  HttpResponse LeavePlayer(netbox_server::SessionId id,
                          netbox_server::PlayerId player);
  HttpResponse ListPlayers(netbox_server::SessionId id);
  HttpResponse GetStream(netbox_server::SessionId id);

  netbox_server::SessionManager& session_manager_;
  netbox_server::RuntimeFactory runtime_factory_;
  netbox_server::StreamFactory stream_factory_;
  std::string stream_backend_name_;
};

}  // namespace netbox_api

#endif  // NETBOX_API_API_GATEWAY_H_
