export interface ApiError {
  code: string;
  message: string;
  details?: Record<string, string[]>;
}

export class ApiHttpError extends Error {
  constructor(
    readonly status: number,
    readonly apiError: ApiError,
  ) {
    super(apiError.message);
    this.name = 'ApiHttpError';
  }
}
