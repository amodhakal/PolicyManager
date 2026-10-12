import { expect } from '@wdio/globals';

import { mintToken } from '../helpers/token';
import { seedToken } from '../helpers/auth';

const signingKey = process.env['E2E_JWT_SIGNING_KEY'] ?? 'e2e-test-signing-key';

describe('Policyholders', () => {
  beforeEach(async () => {
    const token = await mintToken(['Admin'], signingKey);
    await seedToken(token);
    await browser.url('/policyholders');
  });

  it('displays the policyholder list', async () => {
    const table = await $('[data-testid="policyholder-table"]');
    await table.waitForDisplayed({ timeout: 10000 });
    const rows = await table.$$('tbody tr');
    expect(rows.length).toBeGreaterThan(0);
  });

  it('navigates to policyholder detail and back', async () => {
    const table = await $('[data-testid="policyholder-table"]');
    await table.waitForDisplayed({ timeout: 10000 });

    const link = await table.$('[data-testid="policyholder-link"]');
    await link.click();

    await browser.waitUntil(
      async () => (await browser.getUrl()).match(/\/policyholders\/\d+/),
      { timeout: 10000 },
    );

    const heading = await $('h1');
    await heading.waitForDisplayed();
    expect(await heading.getText()).toBeTruthy();

    await browser.back();
    await browser.waitUntil(
      async () => (await browser.getUrl()).includes('/policyholders'),
      { timeout: 10000 },
    );
  });
});
