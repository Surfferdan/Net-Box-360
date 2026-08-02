import { defineConfig } from "vite";

export default defineConfig({
  server: {
    host: true,
    port: 3600,
    watch: {
      ignored: ["**/.api-dev-build/**"],
    },
    proxy: {
      "/api": {
        target: process.env.VITE_DEV_API_TARGET ?? "http://127.0.0.1:5077",
        changeOrigin: true,
      },
      "/ws": {
        target: process.env.VITE_DEV_API_TARGET ?? "http://127.0.0.1:5077",
        ws: true,
        changeOrigin: true,
      },
    },
  },
});
