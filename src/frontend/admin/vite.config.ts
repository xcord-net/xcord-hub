import { defineConfig } from 'vite';
import solidPlugin from 'vite-plugin-solid';
import tailwindcss from '@tailwindcss/vite';
import path from 'path';

export default defineConfig({
  plugins: [solidPlugin(), tailwindcss()],
  resolve: {
    alias: {
      '~': path.resolve(__dirname, 'src'),
      '@generated': path.resolve(__dirname, '../generated'),
    },
  },
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
