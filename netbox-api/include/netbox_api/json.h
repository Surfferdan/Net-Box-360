#ifndef NETBOX_API_JSON_H_
#define NETBOX_API_JSON_H_

#include <sstream>
#include <string>
#include <utility>
#include <vector>

namespace netbox_api {

// Minimal, dependency-free JSON object builder - enough to produce the flat
// response bodies this API needs (no nested objects/arrays beyond a single
// level of array-of-objects, which is all Sessions/Players endpoints
// require). Deliberately not a general-purpose JSON library: netbox-api
// only needs to *emit* JSON, never parse arbitrary JSON, so this keeps the
// project dependency-free.
class JsonValue {
 public:
  static JsonValue String(const std::string& value) {
    JsonValue v;
    v.text_ = "\"" + Escape(value) + "\"";
    return v;
  }
  static JsonValue Number(long long value) {
    JsonValue v;
    v.text_ = std::to_string(value);
    return v;
  }
  static JsonValue Bool(bool value) {
    JsonValue v;
    v.text_ = value ? "true" : "false";
    return v;
  }
  // Pre-rendered raw JSON (used for embedding nested objects/arrays built
  // via JsonObject/JsonArray below).
  static JsonValue Raw(const std::string& raw_json) {
    JsonValue v;
    v.text_ = raw_json;
    return v;
  }

  const std::string& text() const { return text_; }

 private:
  static std::string Escape(const std::string& value) {
    std::string out;
    out.reserve(value.size());
    for (char c : value) {
      if (c == '"' || c == '\\') {
        out.push_back('\\');
      }
      out.push_back(c);
    }
    return out;
  }

  std::string text_;
};

class JsonObject {
 public:
  JsonObject& Set(const std::string& key, JsonValue value) {
    fields_.emplace_back(key, std::move(value));
    return *this;
  }
  JsonObject& Set(const std::string& key, const std::string& value) {
    return Set(key, JsonValue::String(value));
  }
  JsonObject& Set(const std::string& key, const char* value) {
    return Set(key, JsonValue::String(value));
  }
  JsonObject& Set(const std::string& key, long long value) {
    return Set(key, JsonValue::Number(value));
  }
  JsonObject& Set(const std::string& key, int value) {
    return Set(key, JsonValue::Number(value));
  }
  JsonObject& Set(const std::string& key, unsigned long long value) {
    return Set(key, JsonValue::Number(static_cast<long long>(value)));
  }
  JsonObject& Set(const std::string& key, bool value) {
    return Set(key, JsonValue::Bool(value));
  }

  std::string ToString() const {
    std::ostringstream out;
    out << "{";
    for (size_t i = 0; i < fields_.size(); ++i) {
      if (i > 0) out << ",";
      out << "\"" << fields_[i].first << "\":" << fields_[i].second.text();
    }
    out << "}";
    return out.str();
  }

 private:
  std::vector<std::pair<std::string, JsonValue>> fields_;
};

class JsonArray {
 public:
  JsonArray& Add(const std::string& raw_json_object) {
    items_.push_back(raw_json_object);
    return *this;
  }

  std::string ToString() const {
    std::ostringstream out;
    out << "[";
    for (size_t i = 0; i < items_.size(); ++i) {
      if (i > 0) out << ",";
      out << items_[i];
    }
    out << "]";
    return out.str();
  }

 private:
  std::vector<std::string> items_;
};

}  // namespace netbox_api

#endif  // NETBOX_API_JSON_H_
