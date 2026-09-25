import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The API base URL for the .NET backend. During development the frontend proxies
// /api to the backend so there are no CORS concerns.
const apiTarget = process.env.VITE_API_TARGET ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: apiTarget,
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
});
