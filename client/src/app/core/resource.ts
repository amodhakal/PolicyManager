import { DestroyRef, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, of, switchMap, tap } from 'rxjs';

import { ApiError } from './problem-details';

/**
 * The read-a-page-and-handle-failures spine every list page needs, as a plain class rather than a
 * component wrapper.
 *
 * Requests go through a `Subject` fed to `switchMap`, which is what cancels an in-flight request
 * when the user pages again: without it a slow response for page 1 can resolve after a fast one for
 * page 2 and replace it, leaving the table showing a page the user has already moved off.
 *
 * A failure keeps the last good result on screen. Blanking a table the user is reading loses their
 * place, and the banner already says what went wrong.
 */
export class Resource<T> {
  private readonly reload$ = new Subject<void>();

  readonly data = signal<T | null>(null);
  readonly error = signal<ApiError | null>(null);
  readonly loading = signal(true);

  constructor(fetch: () => Observable<T>, destroyRef: DestroyRef) {
    this.reload$
      .pipe(
        switchMap(() => {
          this.loading.set(true);
          this.error.set(null);
          return fetch().pipe(
            tap((value) => {
              this.data.set(value);
              this.loading.set(false);
            }),
            // Caught rather than thrown: an error escaping the stream would complete it, and this
            // subject is the only thing that can issue the next request.
            catchError((failure: unknown) => {
              this.error.set(failure as ApiError);
              this.loading.set(false);
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe();

    this.reload();
  }

  /** Re-issues the current query. Called after a write, so the list reflects it. */
  reload(): void {
    untracked(() => this.reload$.next());
  }
}

/** Convenience for a component that wants a resource without threading a `DestroyRef` in itself. */
export function injectResource<T>(fetch: () => Observable<T>): Resource<T> {
  return new Resource<T>(fetch, inject(DestroyRef));
}
