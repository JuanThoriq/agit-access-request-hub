import { STATUS_LABEL, type AccessRequest } from '../lib/types';

export function DetailFacts({ request }: { request: AccessRequest }) {
  return (
    <dl className="facts">
      <div className="fact">
        <dt>Status</dt>
        <dd>{STATUS_LABEL[request.status]}</dd>
      </div>
      <div className="fact">
        <dt>System owner</dt>
        <dd>{request.application.systemOwner.displayName}</dd>
      </div>
      <div className="fact">
        <dt>Version</dt>
        <dd>{request.version}</dd>
      </div>
      <div className="fact">
        <dt>Policy</dt>
        <dd>{request.policyVersion}</dd>
      </div>
    </dl>
  );
}
