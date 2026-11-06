/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

export default defineConfig(({ mode }) => ({
  // Relative paths: the host serves dist/ from a virtual host or file mapping.
  base: './',
  plugins: [svelte()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'chrome120',
    // design mode = single self-contained file (inlined by scripts/inline-preview.mjs)
    assetsInlineLimit: mode === 'design' ? 100_000_000 : 4096,
    cssCodeSplit: mode !== 'design',
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
}));
