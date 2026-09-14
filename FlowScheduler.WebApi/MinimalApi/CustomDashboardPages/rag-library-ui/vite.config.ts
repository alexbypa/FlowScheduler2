import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  base: '/rag-library/',
  build: {
    outDir: '../../../wwwroot/rag-library',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/rag': {
        target: 'http://localhost:5118',
        changeOrigin: true,
      },
    },
  },
})
