#include "netbox_api/api_gateway.h"

#include "mini_test.h"
#include "mocks.h"

namespace netbox_api {
namespace test {

namespace {

HttpRequest Get(const std::string& path) {
  return HttpRequest{HttpMethod::kGet, path, ""};
}
HttpRequest Post(const std::string& path, const std::string& body = "") {
  return HttpRequest{HttpMethod::kPost, path, body};
}
HttpRequest Delete(const std::string& path) {
  return HttpRequest{HttpMethod::kDelete, path, ""};
}

// Pulls the numeric value out of a flat `"key":123` field in a JSON body -
// enough for these tests without a full JSON parser dependency.
long long ExtractNumber(const std::string& json, const std::string& key) {
  std::string needle = "\"" + key + "\":";
  size_t pos = json.find(needle);
  if (pos == std::string::npos) return -1;
  pos += needle.size();
  size_t end = pos;
  while (end < json.size() &&
        (std::isdigit(static_cast<unsigned char>(json[end])))) {
    ++end;
  }
  return std::stoll(json.substr(pos, end - pos));
}

bool Contains(const std::string& haystack, const std::string& needle) {
  return haystack.find(needle) != std::string::npos;
}

}  // namespace

TEST_CASE("ApiGateway POST /sessions creates a session") {
  netbox_server::SessionManager manager;
  ApiGateway gateway(manager, MakeRuntimeFactory(), MakeStreamFactory());

  HttpResponse response = gateway.HandleRequest(Post("/sessions"));
  REQUIRE(response.status == 200);
  REQUIRE(Contains(response.body, "\"state\":\"Created\""));
  REQUIRE(ExtractNumber(response.body, "id") > 0);
}

TEST_CASE("ApiGateway session lifecycle: start, stop, get, list, destroy") {
  netbox_server::SessionManager manager;
  ApiGateway gateway(manager, MakeRuntimeFactory(), MakeStreamFactory());

  HttpResponse create = gateway.HandleRequest(Post("/sessions"));
  long long id = ExtractNumber(create.body, "id");
  std::string id_str = std::to_string(id);

  HttpResponse start = gateway.HandleRequest(Post("/sessions/" + id_str + "/start"));
  REQUIRE(start.status == 200);
  REQUIRE(Contains(start.body, "\"state\":\"Running\""));

  HttpResponse get = gateway.HandleRequest(Get("/sessions/" + id_str));
  REQUIRE(get.status == 200);
  REQUIRE(Contains(get.body, "\"state\":\"Running\""));

  HttpResponse list = gateway.HandleRequest(Get("/sessions"));
  REQUIRE(list.status == 200);
  REQUIRE(Contains(list.body, id_str));

  HttpResponse stop = gateway.HandleRequest(Post("/sessions/" + id_str + "/stop"));
  REQUIRE(stop.status == 200);
  REQUIRE(Contains(stop.body, "\"state\":\"Stopped\""));

  HttpResponse destroy = gateway.HandleRequest(Delete("/sessions/" + id_str));
  REQUIRE(destroy.status == 204);

  HttpResponse missing = gateway.HandleRequest(Get("/sessions/" + id_str));
  REQUIRE(missing.status == 404);
}

TEST_CASE("ApiGateway reports Conflict when the runtime fails to start") {
  netbox_server::SessionManager manager;
  ApiGateway gateway(manager, MakeRuntimeFactory(/*fail_to_start=*/true),
                     MakeStreamFactory());

  HttpResponse create = gateway.HandleRequest(Post("/sessions"));
  long long id = ExtractNumber(create.body, "id");

  HttpResponse start =
      gateway.HandleRequest(Post("/sessions/" + std::to_string(id) + "/start"));
  REQUIRE(start.status == 409);
}

TEST_CASE("ApiGateway player join/leave") {
  netbox_server::SessionManager manager;
  ApiGateway gateway(manager, MakeRuntimeFactory(), MakeStreamFactory());

  HttpResponse create = gateway.HandleRequest(Post("/sessions"));
  long long id = ExtractNumber(create.body, "id");
  std::string id_str = std::to_string(id);

  HttpResponse join = gateway.HandleRequest(
      Post("/sessions/" + id_str + "/players", "{\"controller_slot\":0}"));
  REQUIRE(join.status == 200);
  long long player_id = ExtractNumber(join.body, "id");
  REQUIRE(player_id > 0);

  HttpResponse list_players =
      gateway.HandleRequest(Get("/sessions/" + id_str + "/players"));
  REQUIRE(list_players.status == 200);
  REQUIRE(Contains(list_players.body, std::to_string(player_id)));

  // Duplicate slot should conflict.
  HttpResponse duplicate = gateway.HandleRequest(
      Post("/sessions/" + id_str + "/players", "{\"controller_slot\":0}"));
  REQUIRE(duplicate.status == 409);

  HttpResponse leave = gateway.HandleRequest(
      Delete("/sessions/" + id_str + "/players/" + std::to_string(player_id)));
  REQUIRE(leave.status == 204);

  HttpResponse list_after =
      gateway.HandleRequest(Get("/sessions/" + id_str + "/players"));
  REQUIRE_FALSE(Contains(list_after.body, std::to_string(player_id)));
}

TEST_CASE("ApiGateway GET stream status matches spec shape") {
  netbox_server::SessionManager manager;
  ApiGateway gateway(manager, MakeRuntimeFactory(), MakeStreamFactory());

  HttpResponse create = gateway.HandleRequest(Post("/sessions"));
  long long id = ExtractNumber(create.body, "id");
  std::string id_str = std::to_string(id);

  HttpResponse before_start =
      gateway.HandleRequest(Get("/sessions/" + id_str + "/stream"));
  REQUIRE(before_start.status == 200);
  REQUIRE(Contains(before_start.body, "\"state\":\"stopped\""));
  REQUIRE(Contains(before_start.body, "\"connection\":\"webrtc\""));

  gateway.HandleRequest(Post("/sessions/" + id_str + "/start"));
  HttpResponse after_start =
      gateway.HandleRequest(Get("/sessions/" + id_str + "/stream"));
  REQUIRE(Contains(after_start.body, "\"state\":\"running\""));
}

}  // namespace test
}  // namespace netbox_api
