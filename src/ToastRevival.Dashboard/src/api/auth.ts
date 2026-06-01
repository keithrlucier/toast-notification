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

// Returned by login when the user has a confirmed authenticator (takes
// precedence over the SMS challenge).
export interface LoginTotpChallenge {
  userId: string;
  step: 'totp_required';
}

export interface LoginTotpVerifyRequest {
  userId: string;
  code: string;
}

export interface MfaStatusResponse {
  enabled: boolean;
  tenantRequired: boolean;
  hasPhone: boolean;
}

// login returns AuthResponse (no second factor), LoginSmsChallenge (phone
// confirmed), or LoginTotpChallenge (authenticator enrolled)
export type LoginResult = AuthResponse | LoginSmsChallenge | LoginTotpChallenge;

export const authApi = {
  login: (req: LoginRequest) =>
    api.post<LoginResult>('/api/auth/login', req),

  loginVerifySms: (req: LoginSmsVerifyRequest) =>
    api.post<AuthResponse>('/api/auth/login/verify-sms', req),

  loginVerifyTotp: (req: LoginTotpVerifyRequest) =>
    api.post<AuthResponse>('/api/auth/login/verify-totp', req),

  register: (req: RegisterRequest) =>
    api.post<AuthResponse>('/api/auth/register', req),

  mfaStatus: () =>
    api.get<MfaStatusResponse>('/api/auth/mfa/status'),

  mfaEnroll: () =>
    api.post<MfaEnrollResponse>('/api/auth/mfa/enroll'),

  mfaEnrollConfirm: (req: MfaVerifyRequest) =>
    api.post<MfaVerifyResponse>('/api/auth/mfa/enroll/confirm', req),

  mfaDisable: (req: MfaVerifyRequest) =>
    api.post<void>('/api/auth/mfa/disable', req),

  mfaVerify: (req: MfaVerifyRequest) =>
    api.post<MfaVerifyResponse>('/api/auth/mfa/verify', req),

  mfaSendSms: () =>
    api.post<{ masked: string }>('/api/auth/mfa/send-sms'),

  mfaVerifySms: (req: MfaVerifyRequest) =>
    api.post<MfaVerifyResponse>('/api/auth/mfa/verify-sms', req),
};
