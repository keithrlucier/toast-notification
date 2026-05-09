import { createContext, useContext, useEffect, useState, ReactNode } from 'react';
import { authApi, type AuthResponse } from '../api/auth';

interface AuthUser {
  userId: string;
  tenantId: string;
  email: string;
  role: string;
  token: string;
  mfaElevated: boolean;
}

interface AuthContextValue {
  user: AuthUser | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (tenantName: string, email: string, password: string) => Promise<void>;
  logout: () => void;
  setMfaToken: (token: string, role: string) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

function parseToken(token: string): { mfaElevated: boolean } {
  try {
    const payload = JSON.parse(atob(token.split('.')[1]!));
    return { mfaElevated: payload['mfa'] === 'true' || payload['mfa'] === true };
  } catch {
    return { mfaElevated: false };
  }
}

function userFromResponse(res: AuthResponse): AuthUser {
  const { mfaElevated } = parseToken(res.token);
  return {
    userId: res.userId,
    tenantId: res.tenantId,
    email: res.email,
    role: res.role,
    token: res.token,
    mfaElevated,
  };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const stored = localStorage.getItem('token');
    const storedUser = localStorage.getItem('user');
    if (stored && storedUser) {
      try {
        const parsed = JSON.parse(storedUser) as AuthUser;
        const { mfaElevated } = parseToken(stored);
        setUser({ ...parsed, token: stored, mfaElevated });
      } catch { /* ignore corrupt storage */ }
    }
    setLoading(false);
  }, []);

  const login = async (email: string, password: string) => {
    const res = await authApi.login({ email, password });
    const u = userFromResponse(res);
    localStorage.setItem('token', res.token);
    localStorage.setItem('user', JSON.stringify(u));
    setUser(u);
  };

  const register = async (tenantName: string, email: string, password: string) => {
    const res = await authApi.register({ tenantName, adminEmail: email, adminPassword: password });
    const u = userFromResponse(res);
    localStorage.setItem('token', res.token);
    localStorage.setItem('user', JSON.stringify(u));
    setUser(u);
  };

  const logout = () => {
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    setUser(null);
  };

  const setMfaToken = (token: string, role: string) => {
    if (!user) return;
    const { mfaElevated } = parseToken(token);
    const updated = { ...user, token, role, mfaElevated };
    localStorage.setItem('token', token);
    localStorage.setItem('user', JSON.stringify(updated));
    setUser(updated);
  };

  return (
    <AuthContext.Provider value={{ user, loading, login, register, logout, setMfaToken }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
