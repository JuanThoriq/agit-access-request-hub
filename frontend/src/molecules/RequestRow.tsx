import { RiskBadge, StatusPill } from '../atoms/Badge';
import { formatTime } from '../lib/format';
import type { AccessRequest } from '../lib/types';

type Props = {
  request: AccessRequest;
  selected: boolean;
  showRequester: boolean;
  onSelect: (id: number) => void;
};

export function RequestRow({ request, selected, showRequester, onSelect }: Props) {
  return (
    <button type="button" className="row" aria-current={selected} onClick={() => onSelect(request.id)}>
      <span className="row-top">
        <span className="row-title">
          #{request.id} · {request.application.name}
        </span>
        <StatusPill status={request.status} />
        <RiskBadge show={request.isHighRisk} />
      </span>
      <span className="row-meta">
        <span>{request.environment}</span>
        <span className="dot">·</span>
        <span>{request.accessLevel}</span>
        {showRequester && (
          <>
            <span className="dot">·</span>
            <span>{request.requester.displayName}</span>
          </>
        )}
        <span className="dot">·</span>
        <span>v{request.version}</span>
        <span className="dot">·</span>
        <span>{formatTime(request.updatedAtUtc)}</span>
      </span>
    </button>
  );
}
