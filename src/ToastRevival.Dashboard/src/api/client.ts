const BASE = '';

function getToken(): string | null {
  return localStorage.getItem('token');
}

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = 'ApiError';
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = getToken();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(init.headers as Record<string, string> | undefined),
  };
  if (token) headers['Authorization'] = `Bearer ${token}`;

  const res = await fetch(`${BASE}${path}`, { ...init, headers });

  if (!res.ok) {
    let message = `HTTP ${res.status}`;
    try {
      const contentType = res.headers.get('content-type') ?? '';
      if (!contentType.includes('application/json')) {
        const text = await res.text();
        if (text.trim()) message = text.trim();
        throw new Error('handled');
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

      if (typeof body === 'string') {
        if (body.trim()) message = body.trim();
        throw new Error('handled');
      }

      // ASP.NET Core ProblemDetails (default for [Required]/[EmailAddress]/etc.
      // validation failures) carries field-level messages under `errors` as
      // Record<string, string[]>. Controllers that return BadRequest(new { errors = [...] })
      // produce a `errors` array of strings. In either case, surface those
      // before the generic `title` boilerplate so the user sees what to fix.
      if (body.errors && typeof body.errors === 'object' && !Array.isArray(body.errors)) {
        const lines = Object.values(body.errors).flat().filter(Boolean);
        if (lines.length > 0) message = lines.join(' ');
      } else if (Array.isArray(body.errors) && body.errors.length > 0) {
        message = body.errors.join(' ');
      } else {
        message = body.detail ?? body.message ?? body.error ?? body.title ?? message;
      }
    } catch { /* ignore parse failures; keep best message */ }
    throw new ApiError(res.status, message);
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
