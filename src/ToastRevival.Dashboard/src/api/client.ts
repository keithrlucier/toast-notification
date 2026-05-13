const BASE = '';

export const AUTH_UNAUTHORIZED_EVENT = 'toast:auth-unauthorized';
export const AUTH_MESSAGE_STORAGE_KEY = 'toast:auth-message';
export const SESSION_EXPIRED_MESSAGE = 'Your session expired. Please sign in again.';

export function getToken(): string | null {
  return localStorage.getItem('token');
}

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = 'ApiError';
  }
}

export function authHeaders(): Record<string, string> {
  const token = getToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

function isAuthEndpoint(path?: string): boolean {
  return Boolean(path?.startsWith('/api/auth/'));
}

function notifyUnauthorized(path?: string): void {
  if (isAuthEndpoint(path)) return;
  if (typeof window === 'undefined') return;
  window.dispatchEvent(new CustomEvent(AUTH_UNAUTHORIZED_EVENT, {
    detail: { message: SESSION_EXPIRED_MESSAGE },
  }));
}

async function errorMessageFromResponse(res: Response, fallback: string): Promise<string> {
  try {
    const contentType = res.headers.get('content-type') ?? '';
    if (!contentType.includes('application/json')) {
      const text = await res.text();
      return text.trim() || fallback;
    }

    const body = await res.json() as
      | string
      | {
          message?: string;
          title?: string;
          detail?: string;
          error?: string;
          errors?: Record<string, string[]> | string[];
        };

    if (typeof body === 'string') return body.trim() || fallback;

    // ASP.NET Core ProblemDetails (default for [Required]/[EmailAddress]/etc.
    // validation failures) carries field-level messages under `errors` as
    // Record<string, string[]>. Controllers that return BadRequest(new { errors = [...] })
    // produce a `errors` array of strings. In either case, surface those
    // before the generic `title` boilerplate so the user sees what to fix.
    if (body.errors && typeof body.errors === 'object' && !Array.isArray(body.errors)) {
      const lines = Object.values(body.errors).flat().filter(Boolean);
      if (lines.length > 0) return lines.join(' ');
    }

    if (Array.isArray(body.errors) && body.errors.length > 0) {
      return body.errors.join(' ');
    }

    return body.detail ?? body.message ?? body.error ?? body.title ?? fallback;
  } catch {
    return fallback;
  }
}

export async function apiErrorFromResponse(
  res: Response,
  path?: string,
  fallback = `HTTP ${res.status}`,
): Promise<ApiError> {
  if (res.status === 401 && !isAuthEndpoint(path)) {
    notifyUnauthorized(path);
    return new ApiError(res.status, SESSION_EXPIRED_MESSAGE);
  }

  return new ApiError(res.status, await errorMessageFromResponse(res, fallback));
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...authHeaders(),
    ...(init.headers as Record<string, string> | undefined),
  };

  const res = await fetch(`${BASE}${path}`, { ...init, headers });

  if (!res.ok) {
    throw await apiErrorFromResponse(res, path);
  }

  if (res.status === 204) return undefined as unknown as T;
  return res.json() as Promise<T>;
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'POST', body: body !== undefined ? JSON.stringify(body) : undefined }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: 'PUT', body: body !== undefined ? JSON.stringify(body) : undefined }),
  delete: <T = void>(path: string) =>
    request<T>(path, { method: 'DELETE' }),
};
