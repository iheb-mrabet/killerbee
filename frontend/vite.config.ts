import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: { '/api': 'http://127.0.0.1:5100', '/auth': 'http://127.0.0.1:5100' }
  },
  build: { outDir: '../src/KillerBee.Local/wwwroot', emptyOutDir: true, sourcemap: false }
});
