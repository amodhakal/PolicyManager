/**
 * Configuration for `ng test`, which in v22 runs Vitest directly rather than through Karma.
 *
 * The `jsdom` environment and the `@angular/compiler` import below are what let a test touch an
 * Angular service at all: the framework ships partially compiled, so a service that pulls in
 * `HttpClient` falls back to the JIT compiler, which is not loaded in a bare Node test run.
 */
import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'jsdom',
    setupFiles: ['src/test-setup.ts'],
    include: ['src/**/*.spec.ts'],
  },
});
