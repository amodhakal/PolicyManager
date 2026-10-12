import type { Options } from '@wdio/types';

export const config: Options.Testrunner = {
  runner: 'local',
  baseUrl: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
  autoCompileOpts: {
    autoCompile: true,
    tsNodeOpts: {
      project: './tsconfig.json',
      transpileOnly: true,
    },
  },
  specs: ['./e2e/specs/**/*.spec.ts'],
  maxInstances: 1,
  capabilities: [
    {
      browserName: 'chrome',
      'goog:chromeOptions': {
        args: ['--headless', '--no-sandbox', '--disable-dev-shm-usage'],
      },
    },
  ],
  logLevel: 'warn',
  bail: 0,
  waitforTimeout: 10000,
  connectionRetryTimeout: 120000,
  connectionRetryCount: 3,
  services: ['selenium-standalone'],
  framework: 'mocha',
  reporters: ['spec'],
  mochaOpts: {
    ui: 'bdd',
    timeout: 60000,
  },
  before: async () => {
    const { seedAll } = await import('./helpers/seed');
    await seedAll();
  },
};
