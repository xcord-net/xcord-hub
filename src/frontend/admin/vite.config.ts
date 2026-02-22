import { defineConfig } from 'vite';
import solidPlugin from 'vite-plugin-solid';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [solidPlugin(), tailwindcss()],
  base: '/admin/',
  server: {
    port: 3002,
    proxy: {
      '/api': 'http://localhost:5100',
    },
  },
  build: {
    target: 'esnext',
    outDir: 'dist',
  },
});
