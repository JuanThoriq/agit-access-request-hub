export type UserRef = { id: number; email: string; displayName: string };

export type ApplicationRef = { id: number; name: string; systemOwner: UserRef };

export type AllowedActions = { canApprove: boolean; canReject: boolean };

export type AuditEvent = {
  id: number;
  eventType: string;
  actor: UserRef;
  fromStatus: string | null;
  toStatus: string;
  reason: string | null;
  atUtc: string;
};

export type AccessRequest = {
  id: number;
  clientRequestId: string;
  requester: UserRef;
  application: ApplicationRef;
  environment: 'NonProduction' | 'Production';
  accessLevel: 'Read' | 'Admin';
  justification: string;
  status: RequestStatus;
  isHighRisk: boolean;
  policyVersion: string;
  version: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  allowedActions: AllowedActions;
  audit: AuditEvent[] | null;
};

export type RequestStatus =
  | 'PendingManagerApproval'
  | 'PendingSystemOwnerApproval'
  | 'Approved'
  | 'Rejected';

export type CurrentUser = {
  user: UserRef;
  isAdmin: boolean;
  ownedApplications: ApplicationRef[];
  directReports: UserRef[];
};

export type CreateRequestInput = {
  clientRequestId: string;
  applicationId: number;
  environment: 'NonProduction' | 'Production';
  accessLevel: 'Read' | 'Admin';
  justification: string;
};

export const DEMO_USERS = [
  { email: 'alice@example.local', label: 'Alice — requester (reports to Bob)' },
  { email: 'bob@example.local', label: 'Bob — manager of Alice, Carol, Dana, Erin' },
  { email: 'carol@example.local', label: 'Carol — system owner CRM' },
  { email: 'dana@example.local', label: 'Dana — system owner Finance Portal' },
  { email: 'erin@example.local', label: 'Erin — admin / auditor' },
] as const;

export const STATUS_LABEL: Record<RequestStatus, string> = {
  PendingManagerApproval: 'Waiting manager',
  PendingSystemOwnerApproval: 'Waiting system owner',
  Approved: 'Approved',
  Rejected: 'Rejected',
};

export const EVENT_LABEL: Record<string, string> = {
  RequestCreated: 'Request created',
  Approved: 'Approved',
  Rejected: 'Rejected',
};
