import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// In development the API runs with `dotnet run` on :5000; in the image, nginx proxies /v1.
// Bound to 127.0.0.1 because Vite otherwise listens on IPv6 only on some Windows machines.
export default defineConfig({
  base: process.env.BASE_PATH ?? "/",
  plugins: [react(), tailwindcss()],
  server: { host: "127.0.0.1", proxy: { "/v1": "http://127.0.0.1:5000" } },
});
