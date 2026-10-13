import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the console runs on :5173 and proxies API calls to the admin API.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
});
