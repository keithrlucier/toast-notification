import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5216',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:5216',
        ws: true,
        changeOrigin: true,
      },
      // Uploaded asset library files are served by the API's static-file
      // middleware. Proxy them in dev so previews load from the same origin.
      '/assets': {
        target: 'http://localhost:5216',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    // Avoid /assets/ path collision with the nginx upload proxy — the asset
    // library serves /assets/{tenantId}/{file} from the API.
    assetsDir: 'static',
    sourcemap: false,
  },
})
