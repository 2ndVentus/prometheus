import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'

export default defineConfig(({ mode }) => {
  // Backend URL for the dev/preview proxy, from the shell or a .env file (loadEnv reads both;
  // plain process.env wouldn't see .env). Same-origin `/api` calls mean no CORS locally.
  const env = loadEnv(mode, process.cwd(), '')
  const proxy = {
    '/api': { target: env.API_PROXY_TARGET || 'http://localhost:5080', changeOrigin: true },
  }

  return {
    plugins: [react(), tailwindcss()],
    server: { port: 5173, strictPort: true, proxy },
    preview: { port: 4173, strictPort: true, proxy },
    test: {
      environment: 'jsdom',
      globals: true,
      globalSetup: ['./vitest.global-setup.ts'],
      setupFiles: ['./src/test/setup.ts'],
      restoreMocks: true,
      coverage: {
        provider: 'v8',
        reporter: ['text', 'html', 'cobertura'],
        include: ['src/**/*.{ts,tsx}'],
        exclude: ['src/main.tsx', 'src/test/**', 'src/**/*.test.{ts,tsx}'],
      },
    },
  }
})
