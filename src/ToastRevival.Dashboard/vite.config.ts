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
    },
  },
  build: {
    outDir: 'dist',
    // Avoid /assets/ path collision with the nginx upload proxy
    // (M5.C asset library serves /assets/{tenantId}/{file} from the API).
    assetsDir: 'static',
    sourcemap: false,
  },
})
