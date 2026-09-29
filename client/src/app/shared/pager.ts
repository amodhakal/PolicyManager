import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { PagedResult } from '../core/models';

/**
 * The page controls under every list, driven by the envelope's own navigation metadata.
 *
 * The previous/next buttons are disabled from `hasPrevious`/`hasNext` rather than from comparing
 * page numbers against `totalPages`, so a list whose last page is partially deleted cannot offer a
 * next page that comes back empty.
 */
@Component({
  selector: 'app-pager',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (result(); as page) {
      <nav class="pager" aria-label="Pagination">
        <p class="summary">
          {{ page.totalCount }} {{ page.totalCount === 1 ? 'result' : 'results' }}
          @if (page.totalPages > 1) {
            <span class="muted">· page {{ page.page }} of {{ page.totalPages }}</span>
          }
        </p>
        <div class="controls">
          <label class="size">
            <span>Per page</span>
            <select
              [value]="page.pageSize"
              (change)="pageSizeChange.emit(+$any($event.target).value)"
            >
              @for (option of sizeOptions; track option) {
                <option [value]="option">{{ option }}</option>
              }
            </select>
          </label>
          <button
            type="button"
            [disabled]="!page.hasPrevious"
            (click)="pageChange.emit(page.page - 1)"
          >
            Previous
          </button>
          <button type="button" [disabled]="!page.hasNext" (click)="pageChange.emit(page.page + 1)">
            Next
          </button>
        </div>
      </nav>
    }
  `,
  styles: `
    .pager {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 1rem;
      flex-wrap: wrap;
      margin-top: 1rem;
      padding-top: 0.75rem;
      border-top: 1px solid #e4e4e7;
    }
    .summary {
      margin: 0;
      font-size: 0.875rem;
      color: #3f3f46;
    }
    .muted {
      color: #71717a;
    }
    .controls {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .size {
      display: flex;
      align-items: center;
      gap: 0.375rem;
      font-size: 0.875rem;
      color: #3f3f46;
    }
  `,
})
export class Pager {
  readonly result = input.required<PagedResult<unknown> | null>();
  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();

  readonly sizeOptions = [10, 25, 50, 100] as const;
  protected readonly _ = computed(() => this.result());
}
