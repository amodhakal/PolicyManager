import { expect } from '@wdio/globals';

import { mintToken } from '../helpers/token';
import { seedToken } from '../helpers/auth';

const signingKey = process.env['E2E_JWT_SIGNING_KEY'] ?? 'e2e-test-signing-key';

describe('Claims', () => {
  beforeEach(async () => {
    const token = await mintToken(['Admin'], signingKey);
    await seedToken(token);
    await browser.url('/claims');
  });

  it('displays the claim list', async () => {
    const table = await $('[data-testid="claim-table"]');
    await table.waitForDisplayed({ timeout: 10000 });
    const rows = await table.$$('tbody tr');
    expect(rows.length).toBeGreaterThan(0);
  });

  it('navigates to claim detail', async () => {
    const table = await $('[data-testid="claim-table"]');
    await table.waitForDisplayed({ timeout: 10000 });

    const link = await table.$('[data-testid="claim-link"]');
    await link.click();

    await browser.waitUntil(
      async () => (await browser.getUrl()).match(/\/claims\/\d+/),
      { timeout: 10000 },
    );

    const heading = await $('h1');
    await heading.waitForDisplayed();
    expect(await heading.getText()).toBeTruthy();
  });
});
