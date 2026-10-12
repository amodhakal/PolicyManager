import { expect } from '@wdio/globals';

import { mintToken } from '../helpers/token';
import { seedToken } from '../helpers/auth';

const signingKey = process.env['E2E_JWT_SIGNING_KEY'] ?? 'e2e-test-signing-key';

describe('Reports', () => {
  beforeEach(async () => {
    const token = await mintToken(['Admin'], signingKey);
    await seedToken(token);
    await browser.url('/reports');
  });

  it('renders the reports page', async () => {
    const heading = await $('h1');
    await heading.waitForDisplayed({ timeout: 10000 });
    expect(await heading.getText()).toContain('Report');
  });

  it('displays the open claims report', async () => {
    const table = await $('[data-testid="open-claims-report"]');
    await table.waitForDisplayed({ timeout: 10000 });
    expect(await table).toBeDisplayed();
  });
});
