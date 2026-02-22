import { defineConfig } from 'vite';
import solidPlugin from 'vite-plugin-solid';
import tailwindcss from '@tailwindcss/vite';

// Dev port 3001, proxies to :5100
export default defineConfig({
  plugins: [solidPlugin(), tailwindcss()],
  server: {
    port: 3001,
    proxy: {
      '/api': 'http://localhost:5100',
    },
  },
  build: {
    target: 'esnext',
    outDir: 'dist',
  },
});
