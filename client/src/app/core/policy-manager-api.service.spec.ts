import { describe, expect, it } from 'vitest';

import { toParams } from './policy-manager-api.service';

/**
 * The query builder is tested through the module's own helper rather than through an HTTP mock,
 * because the thing worth pinning is which options are omitted: an option sent as the literal
 * string "undefined" binds to a string on the server and fails the model's own validation, so an
 * omitted option has to be absent from the query rather than sent empty.
 */
describe('toParams', () => {
  it('serializes the options that were set', () => {
    const params = toParams({ page: 2, pageSize: 50, sortBy: 'lastName', descending: true });

    expect(params.get('page')).toBe('2');
    expect(params.get('pageSize')).toBe('50');
    expect(params.get('sortBy')).toBe('lastName');
    expect(params.get('descending')).toBe('true');
  });

  it('omits undefined, null and empty values', () => {
    const params = toParams({
      page: 1,
      pageSize: undefined,
      sortBy: null as unknown as string,
      descending: false,
      status: '',
    });

    expect(params.get('page')).toBe('1');
    expect(params.has('pageSize')).toBe(false);
    expect(params.has('sortBy')).toBe(false);
    // false is a real instruction (sort ascending), so it stays on the wire as "false".
    expect(params.get('descending')).toBe('false');
    expect(params.has('status')).toBe(false);
  });

  it('produces an empty query when nothing was set', () => {
    const params = toParams({});

    expect(params.keys().length).toBe(0);
  });
});
