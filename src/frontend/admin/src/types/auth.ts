export interface LoginRequest {
  email: string;
  password: string;
  twoFactorCode?: string;
}

export interface AuthTokens {
  userId: string;
  username: string;
  accessToken: string;
}

export interface User {
  id: string;
  username: string;
  email: string;
  isAdmin: boolean;
}
