import { createContext, useCallback, useContext, useEffect, useState, ReactNode } from 'react';
import { authApi, type AuthResponse } from '../api/auth';
import { AUTH_MESSAGE_STORAGE_KEY, AUTH_UNAUTHORIZED_EVENT, SESSION_EXPIRED_MESSAGE } from '../api/client';

interface AuthUser {
  userId: string;
  tenantId: string;
  email: string;
  role: string;
  isPlatformAdmin: boolean;
  token: string;
  mfaElevated: boolean;
}

interface AuthContextValue {
  user: AuthUser | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (tenantName: string, email: string, password: string) => Promise<void>;
  logout: () => void;
  setMfaToken: (token: string) => void;
  setSession: (res: AuthResponse) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);
const TOKEN_EXPIRY_SKEW_MS = 5_000;

interface ParsedToken {
  mfaElevated: boolean;
  isPlatformAdmin: boolean;
  expiresAtMs: number | null;
  valid: boolean;
}

function decodeJwtPayload(token: string): Record<string, unknown> | null {
  try {
    const payload = token.split('.')[1];
    if (!payload) return null;

    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - base64.length % 4) % 4), '=');
    return JSON.parse(atob(padded)) as Record<string, unknown>;
  } catch {
    return null;
  }
}

function parseToken(token: string): ParsedToken {
  const payload = decodeJwtPayload(token);
  if (!payload) {
    return { mfaElevated: false, isPlatformAdmin: false, expiresAtMs: null, valid: false };
  }

  const exp = typeof payload.exp === 'number' ? payload.exp * 1000 : null;
  return {
    mfaElevated: payload['mfa'] === 'true' || payload['mfa'] === true,
    isPlatformAdmin: payload['platformAdmin'] === 'true' || payload['platformAdmin'] === true,
    expiresAtMs: exp,
    valid: exp !== null,
  };
}

function isExpired(token: ParsedToken): boolean {
  return token.expiresAtMs !== null && token.expiresAtMs <= Date.now() + TOKEN_EXPIRY_SKEW_MS;
}

function clearStoredSession(): void {
  localStorage.removeItem('token');
  localStorage.removeItem('user');
}

function storeAuthMessage(message: string): void {
  sessionStorage.setItem(AUTH_MESSAGE_STORAGE_KEY, message);
}

function unauthorizedMessage(event: Event): string {
  if (event instanceof CustomEvent && typeof event.detail?.message === 'string') {
    return event.detail.message;
  }
  return SESSION_EXPIRED_MESSAGE;
}

function userFromResponse(res: AuthResponse): AuthUser {
  const tokenInfo = parseToken(res.token);
  return {
    userId: res.userId,
    tenantId: res.tenantId,
    email: res.email,
    role: res.role,
    isPlatformAdmin: Boolean(res.isPlatformAdmin || tokenInfo.isPlatformAdmin),
    token: res.token,
    mfaElevated: tokenInfo.mfaElevated,
  };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  const clearSession = useCallback((message?: string) => {
    clearStoredSession();
    if (message) storeAuthMessage(message);
    setUser(null);
  }, []);

  useEffect(() => {
    const stored = localStorage.getItem('token');
    const storedUser = localStorage.getItem('user');
    if (stored && storedUser) {
      try {
        const parsed = JSON.parse(storedUser) as AuthUser;
        const tokenInfo = parseToken(stored);
        if (!tokenInfo.valid || isExpired(tokenInfo)) {
          clearStoredSession();
          storeAuthMessage(SESSION_EXPIRED_MESSAGE);
        } else {
          setUser({
            ...parsed,
            token: stored,
            mfaElevated: tokenInfo.mfaElevated,
            isPlatformAdmin: Boolean(parsed.isPlatformAdmin || tokenInfo.isPlatformAdmin),
          });
        }
      } catch {
        clearStoredSession();
      }
    }
    setLoading(false);
  }, []);

  useEffect(() => {
    const handleUnauthorized = (event: Event) => clearSession(unauthorizedMessage(event));
    window.addEventListener(AUTH_UNAUTHORIZED_EVENT, handleUnauthorized);
    return () => window.removeEventListener(AUTH_UNAUTHORIZED_EVENT, handleUnauthorized);
  }, [clearSession]);

  useEffect(() => {
    if (!user?.token) return;

    const tokenInfo = parseToken(user.token);
    if (!tokenInfo.valid || isExpired(tokenInfo)) {
      clearSession(SESSION_EXPIRED_MESSAGE);
      return;
    }

    if (tokenInfo.expiresAtMs === null) return;

    const timeout = window.setTimeout(
      () => clearSession(SESSION_EXPIRED_MESSAGE),
      Math.max(tokenInfo.expiresAtMs - Date.now(), 0),
    );
    return () => window.clearTimeout(timeout);
  }, [clearSession, user?.token]);

  const login = async (email: string, password: string) => {
    const res = await authApi.login({ email, password });
    if ('step' in res) throw new Error('SMS verification required.');
    const u = userFromResponse(res);
    localStorage.setItem('token', res.token);
    localStorage.setItem('user', JSON.stringify(u));
    setUser(u);
  };

  const register = async (tenantName: string, email: string, password: string) => {
    const res = await authApi.register({ tenantName, email, password });
    const u = userFromResponse(res);
    localStorage.setItem('token', res.token);
    localStorage.setItem('user', JSON.stringify(u));
    setUser(u);
  };

  const logout = () => clearSession();

  const setMfaToken = (token: string) => {
    if (!user) return;
    const tokenInfo = parseToken(token);
    if (!tokenInfo.valid || isExpired(tokenInfo)) {
      clearSession(SESSION_EXPIRED_MESSAGE);
      return;
    }

    const updated = {
      ...user,
      token,
      mfaElevated: tokenInfo.mfaElevated,
      isPlatformAdmin: Boolean(user.isPlatformAdmin || tokenInfo.isPlatformAdmin),
    };
    localStorage.setItem('token', token);
    localStorage.setItem('user', JSON.stringify(updated));
    setUser(updated);
  };

  const setSession = (res: AuthResponse) => {
    const u = userFromResponse(res);
    localStorage.setItem('token', res.token);
    localStorage.setItem('user', JSON.stringify(u));
    setUser(u);
  };

  return (
    <AuthContext.Provider value={{ user, loading, login, register, logout, setMfaToken, setSession }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
