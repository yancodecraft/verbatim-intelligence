import { fileURLToPath, URL } from "node:url";
import vue from "@vitejs/plugin-vue";
import { defineConfig } from "vite";
import vueDevTools from "vite-plugin-vue-devtools";

// https://vite.dev/config/
export default defineConfig({
	plugins: [vue(), vueDevTools()],
	resolve: {
		alias: {
			"@": fileURLToPath(new URL("./src", import.meta.url)),
		},
	},
	server: {
		// The e2e container reaches the dev server as http://frontend:5173.
		allowedHosts: ["frontend"],
		// Same routing contract as the production reverse proxy: the app
		// calls /api/*, the proxy strips the prefix and forwards to the
		// backend — no CORS anywhere.
		proxy: {
			"/api": {
				target: "http://backend:8080",
				rewrite: (path) => path.replace(/^\/api/, ""),
			},
		},
	},
});
