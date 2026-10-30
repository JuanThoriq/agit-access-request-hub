import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The API is the only trust boundary; during development Vite proxies /api to it so the
// browser never needs CORS and the frontend ships as static files.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
});
