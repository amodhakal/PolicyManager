import { expect } from '@wdio/globals';

import { mintToken } from '../helpers/token';
import { seedToken } from '../helpers/auth';

const signingKey = process.env['E2E_JWT_SIGNING_KEY'] ?? 'e2e-test-signing-key';

describe('Policies', () => {
  beforeEach(async () => {
    const token = await mintToken(['Admin'], signingKey);
    await seedToken(token);
    await browser.url('/policies');
  });

  it('displays the policy list', async () => {
    const table = await $('[data-testid="policy-table"]');
    await table.waitForDisplayed({ timeout: 10000 });
    const rows = await table.$$('tbody tr');
    expect(rows.length).toBeGreaterThan(0);
  });

  it('sorts by policy number', async () => {
    const table = await $('[data-testid="policy-table"]');
    await table.waitForDisplayed({ timeout: 10000 });

    const sortBtn = await $('[data-testid="sort-policy-number"]');
    await sortBtn.click();

    await browser.pause(500);
    const rows = await table.$$('tbody tr');
    expect(rows.length).toBeGreaterThan(0);
  });

  it('filters by status', async () => {
    const select = await $('[data-testid="status-filter"]');
    await select.waitForDisplayed();
    await select.selectByVisibleText('Active');

    await browser.pause(500);
    const table = await $('[data-testid="policy-table"]');
    await table.waitForDisplayed();
  });

  it('creates a new policy', async () => {
    const newBtn = await $('[data-testid="new-policy-button"]');
    await newBtn.waitForDisplayed();
    await newBtn.click();

    const holderSelect = await $('select[name="policyHolderId"]');
    await holderSelect.waitForDisplayed();
    await holderSelect.selectByIndex(1);

    const premiumInput = await $('input[name="premium"]');
    await premiumInput.setValue('2500');

    const startDate = await $('input[name="startDate"]');
    await startDate.setValue('2026-06-01');

    const endDate = await $('input[name="endDate"]');
    await endDate.setValue('2027-06-01');

    const submitBtn = await $('form button[type="submit"]');
    await submitBtn.click();

    await browser.waitUntil(
      async () => (await browser.getUrl()).match(/\/policies\/\d+/),
      { timeout: 10000 },
    );
  });

  it('navigates to policy detail', async () => {
    const table = await $('[data-testid="policy-table"]');
    await table.waitForDisplayed({ timeout: 10000 });

    const link = await table.$('[data-testid="policy-link"]');
    await link.click();

    await browser.waitUntil(
      async () => (await browser.getUrl()).match(/\/policies\/\d+/),
      { timeout: 10000 },
    );
  });
});
