export interface AdminSummary {
  id: string;
  email: string;
  fullName: string | null;
}

export interface CurrentAdmin extends AdminSummary {
  lastLoginAt: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresIn: number;
  admin: AdminSummary;
}

export interface TokenResponse {
  accessToken: string;
  expiresIn: number;
}

export type AuthenticationStatus = 'unknown' | 'initializing' | 'authenticated' | 'anonymous';
