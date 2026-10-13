// Typed client for the admin API. All calls go to /api on the same origin. The session lives in an
// HttpOnly cookie that the browser sends automatically; this code never sees it.

export type Tenant = {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  rateLimitPerMinute: number | null;
  allowedIpRanges: string[];
  activeKeyCount: number;
  createdAt: string;
  updatedAt: string;
};

export type TenantInput = {
  name: string;
  description: string | null;
  isActive: boolean;
  rateLimitPerMinute: number | null;
  allowedIpRanges: string[];
};

export type KeyStatus = 'active' | 'revoked' | 'expired';

export type ApiKey = {
  id: string;
  keyId: string;
  name: string;
  scopes: string[];
  status: KeyStatus;
  createdAt: string;
  expiresAt: string | null;
  revokedAt: string | null;
  lastUsedAt: string | null;
};

export type ApiKeyInput = {
  name: string;
  scopes: string[];
  expiresAt: string | null;
};

export type CreatedApiKey = { key: ApiKey; plainKey: string };

export type Destination = { name: string; address: string };

export type Cluster = {
  id: string;
  loadBalancingPolicy: string | null;
  healthCheckEnabled: boolean;
  healthCheckPath: string | null;
  healthCheckIntervalSeconds: number | null;
  destinations: Destination[];
  routeCount: number;
  updatedAt: string;
};

export type ClusterInput = Omit<Cluster, 'routeCount' | 'updatedAt'>;

export type Route = {
  id: string;
  clusterId: string;
  path: string;
  methods: string[];
  order: number | null;
  requiredScope: string | null;
  allowAnonymous: boolean;
  transformsJson: string | null;
  isEnabled: boolean;
  description: string | null;
  updatedAt: string;
};

export type RouteInput = Omit<Route, 'updatedAt'>;

export type Scope = {
  name: string;
  description: string | null;
  routeCount: number;
  activeKeyCount: number;
  createdAt: string;
  updatedAt: string;
};

/** Grants every scope. Not part of the catalog. */
export const ALL_SCOPES = '*';

export type CurrentUser = { id: string; username: string; mustChangePassword: boolean };

export type AdminUser = {
  id: string;
  username: string;
  isActive: boolean;
  mustChangePassword: boolean;
  isLocked: boolean;
  lastSignInAt: string | null;
  createdAt: string;
};

export class ApiError extends Error {
  readonly status: number;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.status = status;
    this.fieldErrors = fieldErrors;
  }
}

export function messageOf(error: unknown): string {
  if (error instanceof Error) return error.message;
  return 'Something went wrong. Try again.';
}

let onUnauthorized: () => void = () => {};
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

type ProblemBody = { title?: string; detail?: string; errors?: Record<string, string[]> };

async function request<T>(
  method: string,
  path: string,
  body?: unknown,
  options: { signOutOn401?: boolean } = {},
): Promise<T> {
  // The admin API rejects data-changing requests without this header, which blocks cross-site request forgery.
  const headers: Record<string, string> = { 'X-Requested-With': 'enterprise-apim-console' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      method,
      headers,
      credentials: 'same-origin',
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, 'The admin API is not reachable. Check that it is running.');
  }

  if (response.status === 401 && options.signOutOn401 !== false) {
    onUnauthorized();
    throw new ApiError(401, 'Your session has ended. Sign in again.');
  }

  const text = await response.text();
  if (!response.ok) {
    let problem: ProblemBody = {};
    try {
      problem = text ? (JSON.parse(text) as ProblemBody) : {};
    } catch {
      /* not JSON */
    }
    const hasFieldErrors = problem.errors && Object.keys(problem.errors).length > 0;
    const message = hasFieldErrors
      ? 'Some fields need attention.'
      : problem.detail ?? problem.title ?? `The request failed (HTTP ${response.status}).`;
    throw new ApiError(response.status, message, problem.errors ?? {});
  }

  return (text ? JSON.parse(text) : undefined) as T;
}

export const api = {
  scopes: {
    list: () => request<Scope[]>('GET', '/scopes'),
    create: (name: string, description: string | null) => request<Scope>('POST', '/scopes', { name, description }),
    update: (name: string, description: string | null) =>
      request<Scope>('PUT', `/scopes/${encodeURIComponent(name)}`, { description }),
    remove: (name: string) => request<void>('DELETE', `/scopes/${encodeURIComponent(name)}`),
  },

  auth: {
    me: () => request<CurrentUser>('GET', '/auth/me', undefined, { signOutOn401: false }),
    signIn: (username: string, password: string) =>
      request<CurrentUser>('POST', '/auth/sign-in', { username, password }, { signOutOn401: false }),
    signOut: () => request<void>('POST', '/auth/sign-out'),
    changePassword: (currentPassword: string, newPassword: string) =>
      request<CurrentUser>('POST', '/auth/password', { currentPassword, newPassword }),
  },

  users: {
    list: () => request<AdminUser[]>('GET', '/users'),
    create: (username: string, password: string) => request<AdminUser>('POST', '/users', { username, password }),
    enable: (id: string) => request<AdminUser>('POST', `/users/${id}/enable`),
    disable: (id: string) => request<AdminUser>('POST', `/users/${id}/disable`),
    resetPassword: (id: string, password: string) =>
      request<AdminUser>('POST', `/users/${id}/reset-password`, { password }),
    remove: (id: string) => request<void>('DELETE', `/users/${id}`),
  },

  tenants: {
    list: () => request<Tenant[]>('GET', '/tenants'),
    get: (id: string) => request<Tenant>('GET', `/tenants/${id}`),
    create: (input: TenantInput) => request<Tenant>('POST', '/tenants', input),
    update: (id: string, input: TenantInput) => request<Tenant>('PUT', `/tenants/${id}`, input),
    remove: (id: string) => request<void>('DELETE', `/tenants/${id}`),
  },

  keys: {
    list: (tenantId: string) => request<ApiKey[]>('GET', `/tenants/${tenantId}/keys`),
    create: (tenantId: string, input: ApiKeyInput) =>
      request<CreatedApiKey>('POST', `/tenants/${tenantId}/keys`, input),
    update: (tenantId: string, keyId: string, input: ApiKeyInput) =>
      request<ApiKey>('PUT', `/tenants/${tenantId}/keys/${encodeURIComponent(keyId)}`, input),
    revoke: (tenantId: string, keyId: string) =>
      request<ApiKey>('POST', `/tenants/${tenantId}/keys/${encodeURIComponent(keyId)}/revoke`),
  },

  clusters: {
    list: () => request<Cluster[]>('GET', '/clusters'),
    create: (input: ClusterInput) => request<Cluster>('POST', '/clusters', input),
    update: (id: string, input: ClusterInput) =>
      request<Cluster>('PUT', `/clusters/${encodeURIComponent(id)}`, input),
    remove: (id: string) => request<void>('DELETE', `/clusters/${encodeURIComponent(id)}`),
  },

  routes: {
    list: () => request<Route[]>('GET', '/routes'),
    create: (input: RouteInput) => request<Route>('POST', '/routes', input),
    update: (id: string, input: RouteInput) =>
      request<Route>('PUT', `/routes/${encodeURIComponent(id)}`, input),
    remove: (id: string) => request<void>('DELETE', `/routes/${encodeURIComponent(id)}`),
  },

  reloadGateways: () => request<void>('POST', '/gateway/reload'),
};
