import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// During local dev the SPA runs on :5173 and the Azure Functions host on :7071.
// Proxy /api to the Functions host so the browser makes same-origin calls (no CORS),
// matching the deployed Static Web Apps topology where /api is co-located.
export default defineConfig({
  plugins: [react()],
  // Inject the build timestamp so the About page can show an always-accurate
  // "last updated" date. `new Date()` runs in Node at build time.
  define: {
    __BUILD_DATE__: JSON.stringify(new Date().toISOString()),
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:7071',
        changeOrigin: true,
      },
    },
  },
})
