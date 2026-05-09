import { api } from './client';

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  tenantName: string;
  email: string;
  password: string;
  /** Optional explicit subdomain. If omitted, the API derives one from tenantName. */
  subdomain?: string;
}

export interface AuthResponse {
  token: string;
  refreshToken: string;
  expiresAt: string;
  userId: string;
  tenantId: string;
  email: string;
  role: string;
  isPlatformAdmin: boolean;
}

export interface MfaEnrollResponse {
  secret: string;
  qrUri: string;
}

export interface MfaVerifyRequest {
  code: string;
}

export interface MfaVerifyResponse {
  mfaToken: string;
  expiresAt: string;
}

export const authApi = {
  login: (req: LoginRequest) =>
    api.post<AuthResponse>('/api/auth/login', req),

  register: (req: RegisterRequest) =>
    api.post<AuthResponse>('/api/auth/register', req),

  mfaEnroll: () =>
    api.post<MfaEnrollResponse>('/api/auth/mfa/enroll'),

  mfaVerify: (req: MfaVerifyRequest) =>
    api.post<MfaVerifyResponse>('/api/auth/mfa/verify', req),
};
