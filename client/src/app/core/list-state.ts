import { Injectable, signal } from '@angular/core';
import { PageQuery } from './models';

import { DEFAULT_PAGE_SIZE } from './models';

/**
 * The paging, sorting and filtering state of one list view.
 *
 * Kept as signals so a component reads it in a template without a subscription, and so `reset()`
 * restoring the defaults is one assignment rather than a field-by-field reset that a new filter
 * would silently be left out of.
 */
@Injectable()
export class ListState {
  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortBy = signal<string | undefined>(undefined);
  readonly descending = signal(false);
  readonly status = signal<string | undefined>(undefined);

  constructor() {
    this.pageSize.set(DEFAULT_PAGE_SIZE);
  }

  /** The query for the current state. Passed straight to the API service. */
  toQuery(): PageQuery {
    return {
      page: this.page(),
      pageSize: this.pageSize(),
      sortBy: this.sortBy(),
      descending: this.descending() ? true : undefined,
      status: this.status(),
    };
  }

  goToPage(page: number): void {
    // A page below 1 is not a page; clamping here keeps the API from being asked for one.
    this.page.set(Math.max(1, page));
  }

  setPageSize(size: number): void {
    this.pageSize.set(size);
    // The caller is now looking at a different slice, so staying on page 5 of the old size would
    // show them past the end of the new one.
    this.page.set(1);
  }

  /**
   * Sorts by a field, toggling direction when it is already the sort field.
   *
   * The API appends the identifier as a tie-breaker, so the same page number cannot return
   * different rows twice — the order is total without the UI having to sort anything itself.
   */
  sortByField(field: string): void {
    if (this.sortBy() === field) {
      this.descending.update((current) => !current);
    } else {
      this.sortBy.set(field);
      this.descending.set(false);
    }
    this.page.set(1);
  }

  setStatus(status: string | undefined): void {
    this.status.set(status);
    this.page.set(1);
  }

  reset(): void {
    this.page.set(1);
    this.pageSize.set(DEFAULT_PAGE_SIZE);
    this.sortBy.set(undefined);
    this.descending.set(false);
    this.status.set(undefined);
  }
}
