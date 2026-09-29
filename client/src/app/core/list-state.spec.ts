import { describe, expect, it } from 'vitest';

import { ListState } from './list-state';

describe('ListState', () => {
  it('starts on the first page at the default size with no sort', () => {
    const state = new ListState();

    expect(state.toQuery()).toEqual({
      page: 1,
      pageSize: 25,
      sortBy: undefined,
      descending: undefined,
      status: undefined,
    });
  });

  it('omits descending rather than sending false', () => {
    // `descending: false` and an absent `descending` mean the same thing to the API, but sending
    // the literal string "false" through a query string is noise the model has to re-parse.
    const state = new ListState();

    expect(state.toQuery()['descending']).toBeUndefined();
  });

  it('reverses direction when the active sort field is chosen again', () => {
    const state = new ListState();

    state.sortByField('lastName');
    expect(state.sortBy()).toBe('lastName');
    expect(state.descending()).toBe(false);

    state.sortByField('lastName');
    expect(state.descending()).toBe(true);
  });

  it('starts a new sort ascending when the field changes', () => {
    const state = new ListState();

    state.sortByField('lastName');
    state.sortByField('lastName');
    state.sortByField('email');

    expect(state.sortBy()).toBe('email');
    expect(state.descending()).toBe(false);
  });

  it('returns to the first page when the sort or filter changes', () => {
    // Page 5 of a sorted list is not page 5 of a differently sorted one; staying put would show a
    // page from the new ordering that the user never asked for.
    const state = new ListState();
    state.goToPage(5);

    state.sortByField('lastName');
    expect(state.page()).toBe(1);

    state.goToPage(4);
    state.setStatus('Active');
    expect(state.page()).toBe(1);
  });

  it('returns to the first page when the page size changes', () => {
    const state = new ListState();
    state.goToPage(3);

    state.setPageSize(10);

    expect(state.page()).toBe(1);
    expect(state.pageSize()).toBe(10);
  });

  it('refuses a page below the first', () => {
    const state = new ListState();

    state.goToPage(0);

    expect(state.page()).toBe(1);
  });

  it('restores every field on reset', () => {
    const state = new ListState();
    state.goToPage(3);
    state.setPageSize(50);
    state.sortByField('email');
    state.sortByField('email');
    state.setStatus('Active');

    state.reset();

    expect(state.toQuery()).toEqual({
      page: 1,
      pageSize: 25,
      sortBy: undefined,
      descending: undefined,
      status: undefined,
    });
  });
});
