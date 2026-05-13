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

export interface LoginSmsChallenge {
  userId: string;
  step: 'sms_required';
  maskedPhone: string;
}

export interface LoginSmsVerifyRequest {
  userId: string;
  code: string;
}

// login returns either AuthResponse (no phone) or LoginSmsChallenge (phone confirmed)
export type LoginResult = AuthResponse | LoginSmsChallenge;

export const authApi = {
  login: (req: LoginRequest) =>
    api.post<LoginResult>('/api/auth/login', req),

  loginVerifySms: (req: LoginSmsVerifyRequest) =>
    api.post<AuthResponse>('/api/auth/login/verify-sms', req),

  register: (req: RegisterRequest) =>
    api.post<AuthResponse>('/api/auth/register', req),

  mfaEnroll: () =>
    api.post<MfaEnrollResponse>('/api/auth/mfa/enroll'),

  mfaVerify: (req: MfaVerifyRequest) =>
    api.post<MfaVerifyResponse>('/api/auth/mfa/verify', req),

  mfaSendSms: () =>
    api.post<{ masked: string }>('/api/auth/mfa/send-sms'),

  mfaVerifySms: (req: MfaVerifyRequest) =>
    api.post<MfaVerifyResponse>('/api/auth/mfa/verify-sms', req),
};
