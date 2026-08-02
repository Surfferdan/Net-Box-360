#ifndef NETBOX_API_HTTP_TYPES_H_
#define NETBOX_API_HTTP_TYPES_H_

#include <string>
#include <unordered_map>

namespace netbox_api {

// Transport-agnostic request/response boundary. netbox-api deliberately
// does not bind to any concrete HTTP server/socket library - a future
// transport adapter (outside this project, e.g. a thin wrapper around any
// HTTP server framework) is responsible for turning real wire-level HTTP
// requests into an HttpRequest and writing an HttpResponse back to the
// socket. This keeps ApiGateway's routing/business logic fully unit
// testable without a real network stack.
enum class HttpMethod {
  kGet,
  kPost,
  kDelete,
};

struct HttpRequest {
  HttpMethod method = HttpMethod::kGet;
  std::string path;
  std::string body;  // Raw JSON body, if any (POST only in this API).
};

struct HttpResponse {
  int status = 200;
  std::string body;  // Raw JSON body.

  static HttpResponse Ok(const std::string& json_body) {
    return HttpResponse{200, json_body};
  }
  static HttpResponse NotFound() {
    return HttpResponse{404, "{\"error\":\"not_found\"}"};
  }
  static HttpResponse BadRequest(const std::string& message = "bad_request") {
    return HttpResponse{400, "{\"error\":\"" + message + "\"}"};
  }
  static HttpResponse Conflict(const std::string& message = "conflict") {
    return HttpResponse{409, "{\"error\":\"" + message + "\"}"};
  }
};

}  // namespace netbox_api

#endif  // NETBOX_API_HTTP_TYPES_H_
