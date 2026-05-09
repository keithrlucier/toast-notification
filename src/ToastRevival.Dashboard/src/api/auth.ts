import { api } from './client';

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  tenantName: string;
  adminEmail: string;
  adminPassword: string;
}

export interface AuthResponse {
  token: string;
  userId: string;
  tenantId: string;
  email: string;
  role: string;
}

export interface MfaEnrollResponse {
  secret: string;
  otpauthUri: string;
}

export interface MfaVerifyRequest {
  code: string;
}

export const authApi = {
  login: (req: LoginRequest) =>
    api.post<AuthResponse>('/api/auth/login', req),

  register: (req: RegisterRequest) =>
    api.post<AuthResponse>('/api/auth/register', req),

  mfaEnroll: () =>
    api.post<MfaEnrollResponse>('/api/auth/mfa/enroll'),

  mfaVerify: (req: MfaVerifyRequest) =>
    api.post<AuthResponse>('/api/auth/mfa/verify', req),
};
