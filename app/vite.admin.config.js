import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// Admin local build — separate from the booth app build.
export default defineConfig({
  root: fileURLToPath(new URL('./admin', import.meta.url)),
  plugins: [vue()],
  base: '/admin/',
  build: {
    outDir: fileURLToPath(new URL('./admin/dist', import.meta.url)),
    emptyOutDir: true,
    chunkSizeWarningLimit: 1500,
  },
  server: {
    host: '127.0.0.1',
    port: 5199,
    proxy: {
      '/admin/': 'http://127.0.0.1:8737',
      '/settings': 'http://127.0.0.1:8737',
      '/print': 'http://127.0.0.1:8737',
      '/camera': 'http://127.0.0.1:8737',
      '/capture': 'http://127.0.0.1:8737',
      '/health': 'http://127.0.0.1:8737',
    },
  },
})
