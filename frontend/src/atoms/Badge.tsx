import { STATUS_LABEL, type RequestStatus } from '../lib/types';

export function StatusPill({ status }: { status: RequestStatus }) {
  return (
    <span className="pill" data-status={status}>
      {STATUS_LABEL[status]}
    </span>
  );
}

/** High risk is Production OR Admin: the request needs a second approval by the system owner. */
export function RiskBadge({ show }: { show: boolean }) {
  return show ? <span className="badge risk">high risk</span> : null;
}
