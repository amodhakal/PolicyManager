import { describe, expect, it } from 'vitest';

import { ApiError, toApiError } from './problem-details';

describe('toApiError', () => {
  it('reads a problem details body', () => {
    const error = toApiError(
      409,
      {
        title: 'Conflict',
        detail: 'An adjudicated claim cannot be deleted.',
        correlationId: 'abc-123',
      },
      'fallback',
    );

    expect(error.status).toBe(409);
    expect(error.message).toBe('An adjudicated claim cannot be deleted.');
    expect(error.correlationId).toBe('abc-123');
  });

  it('prefers detail over title, because detail says what happened', () => {
    const error = toApiError(404, { title: 'Not Found', detail: 'No such holder.' }, 'fallback');

    expect(error.message).toBe('No such holder.');
  });

  it('falls back to title when there is no detail', () => {
    const error = toApiError(403, { title: 'Forbidden' }, 'fallback');

    expect(error.message).toBe('Forbidden');
  });

  it('survives a body that is not problem details at all', () => {
    // A proxy's HTML 502 arrives as a string. Parsing it must not throw, and the status must
    // still be reported rather than swallowed.
    const error = toApiError(502, '<html>Bad Gateway</html>', 'The gateway failed.');

    expect(error.status).toBe(502);
    expect(error.correlationId).toBeNull();
    expect(error.errors).toEqual({});
    expect(error.displayMessage).toBe('The gateway failed.');
  });

  it('carries the field errors from a validation problem', () => {
    const error = toApiError(
      400,
      {
        title: 'One or more validation errors occurred.',
        errors: { email: ['Not a valid email.'] },
      },
      'fallback',
    );

    expect(error.errors['email']).toEqual(['Not a valid email.']);
  });
});

describe('ApiError.displayMessage', () => {
  it('lists field errors ahead of the summary', () => {
    // A 400's title is the same sentence for every field error, so surfacing it alone tells the
    // user nothing about what to change.
    const error = new ApiError(
      400,
      {
        title: 'One or more validation errors occurred.',
        errors: { email: ['Not a valid email.'], lastName: ['Too long.'] },
      },
      'fallback',
    );

    expect(error.displayMessage).toBe('email: Not a valid email.; lastName: Too long.');
  });

  it('uses the message when there are no field errors', () => {
    const error = new ApiError(404, { detail: 'No such policy.' }, 'fallback');

    expect(error.displayMessage).toBe('No such policy.');
  });
});
