import { ApiHttpError } from '../../../core/api/api-error.model';
import { ApiResponse } from '../../../core/api/api-response.model';

export function requireData<T>(response: ApiResponse<T>): T { if (!response.success || response.data === undefined) throw new Error('The API returned an invalid success response.'); return response.data; }
export function safeAdminError(error: unknown, fallback = 'The request could not be completed.'): string { return error instanceof ApiHttpError ? error.apiError.message : fallback; }
export function backendFieldError(error: unknown, field: string): string | null { if (!(error instanceof ApiHttpError)) return null; const details = error.apiError.details ?? {}; return details[field]?.[0] ?? details[field.charAt(0).toUpperCase() + field.slice(1)]?.[0] ?? null; }
export function nullable(value: string): string | null { const clean = value.trim(); return clean || null; }
export function isSafeAdminUrl(value: string): boolean { if (!value) return true; try { return ['http:', 'https:', 'mailto:'].includes(new URL(value).protocol.toLowerCase()); } catch { return false; } }
