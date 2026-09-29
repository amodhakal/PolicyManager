/**
 * RFC 7807 problem details, which is what every failure leaves the API as.
 *
 * The API adds a `correlationId` extension to each one (`Program.cs` customizes ProblemDetails), so
 * the UI can show an operator something they can quote in a log search. `errors` is the
 * `ValidationProblemDetails` field and is only present on 400s.
 */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
}

/** A failure the UI knows how to render, carrying the parsed problem details when there were any. */
export class ApiError extends Error {
  readonly status: number;
  readonly correlationId: string | null;
  readonly errors: Record<string, string[]>;

  constructor(status: number, problem: ProblemDetails | null, fallback: string) {
    super(problem?.detail || problem?.title || fallback);
    this.name = 'ApiError';
    this.status = status;
    this.correlationId = problem?.correlationId ?? null;
    this.errors = problem?.errors ?? {};
  }

  /**
   * A single line describing the failure, preferring the field-level validation messages because
   * they say what to change rather than that something is wrong.
   */
  get displayMessage(): string {
    const fieldErrors = Object.entries(this.errors).flatMap(([field, messages]) =>
      messages.map((message) => `${field}: ${message}`),
    );
    if (fieldErrors.length > 0) {
      return fieldErrors.join('; ');
    }
    return this.message;
  }
}

/**
 * Turns any `HttpErrorResponse` into an `ApiError`, tolerating the responses that are not
 * problem details at all — a gateway 502, or a body the framework replaced with HTML.
 */
export function toApiError(status: number, body: unknown, fallback: string): ApiError {
  if (body && typeof body === 'object' && ('title' in body || 'detail' in body)) {
    return new ApiError(status, body as ProblemDetails, fallback);
  }
  return new ApiError(status, null, fallback);
}
