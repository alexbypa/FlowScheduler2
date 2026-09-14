import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  base: '/mcp-playground/',
  build: {
    outDir: '../../../wwwroot/mcp-playground',
    emptyOutDir: true,
  },
  server: {
    port: 5174,
    proxy: {
      '/mcp': {
        target: 'http://localhost:5118',
        changeOrigin: true,
        bypass: (req) => {
          // Don't proxy /mcp-playground/* — only proxy /mcp endpoint
          if (req.url?.startsWith('/mcp-playground')) return req.url
        },
      },
    },
  },
})
