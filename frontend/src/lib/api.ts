import type {
  AccessRequest,
  ApplicationRef,
  CreateRequestInput,
  CurrentUser,
} from './types';

/**
 * The demo identity travels in a header. It is not a security boundary on the client: the server
 * resolves this to a seeded user and enforces every rule again, so the UI can only ever *show*
 * what the backend already allows.
 */
export const IDENTITY_HEADER = 'X-Demo-User';

export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, code: string, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.fieldErrors = fieldErrors;
  }
}

export const IDENTITY_STORAGE_KEY = 'arh.email';

const identity = () => localStorage.getItem(IDENTITY_STORAGE_KEY) ?? '';

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      [IDENTITY_HEADER]: identity(),
      ...(init.headers ?? {}),
    },
  });

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  const body = text ? JSON.parse(text) : null;

  if (!response.ok) {
    // ProblemDetails: { status, title, detail, code, errors? }
    throw new ApiError(
      response.status,
      body?.code ?? 'unknown_error',
      body?.detail ?? body?.title ?? `Request failed with status ${response.status}`,
      body?.errors ?? {},
    );
  }

  return body as T;
}

export const api = {
  me: () => request<CurrentUser>('/api/me'),

  applications: () => request<ApplicationRef[]>('/api/applications'),

  myRequests: () => request<AccessRequest[]>('/api/requests'),

  allRequests: () => request<AccessRequest[]>('/api/requests/all'),

  inbox: () => request<AccessRequest[]>('/api/inbox'),

  detail: (id: number) => request<AccessRequest>(`/api/requests/${id}`),

  create: (input: CreateRequestInput) =>
    request<AccessRequest>('/api/requests', { method: 'POST', body: JSON.stringify(input) }),

  approve: (id: number, version: number) =>
    request<AccessRequest>(`/api/requests/${id}/approve`, {
      method: 'POST',
      body: JSON.stringify({ version }),
    }),

  reject: (id: number, version: number, reason: string) =>
    request<AccessRequest>(`/api/requests/${id}/reject`, {
      method: 'POST',
      body: JSON.stringify({ version, reason }),
    }),
};
