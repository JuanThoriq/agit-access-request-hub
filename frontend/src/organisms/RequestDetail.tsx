import { useRef, useState } from 'react';
import { RiskBadge, StatusPill } from '../atoms/Badge';
import { Banner, type Notice } from '../atoms/Banner';
import { Button } from '../atoms/Button';
import { Skeleton } from '../atoms/Skeleton';
import { AuditTimelineItem } from '../molecules/AuditTimelineItem';
import { EmptyState } from '../molecules/EmptyState';
import { formatTime } from '../lib/format';
import { useFadeIn, useStaggerIn } from '../lib/motion';
import type { AccessRequest } from '../lib/types';
import { DetailFacts } from './DetailFacts';
import { RejectForm } from './RejectForm';

type Props = {
  request: AccessRequest | null;
  loading: boolean;
  busy: boolean;
  notice: Notice | null;
  onApprove: () => void;
  onReject: (reason: string) => void;
  onRefresh: () => void;
};

export function RequestDetail({ request, loading, busy, notice, onApprove, onReject, onRefresh }: Props) {
  const [rejecting, setRejecting] = useState(false);

  const cardRef = useRef<HTMLElement>(null);
  const timelineRef = useRef<HTMLUListElement>(null);

  // Hooks run before any early return: the panel fades when a different request is selected,
  // and the timeline staggers when new audit rows arrive.
  useFadeIn(cardRef, request?.id ?? 'empty');
  useStaggerIn(timelineRef, 'li', `${request?.id ?? 'none'}:${request?.audit?.length ?? 0}`);

  if (loading && !request) {
    return (
      <section className="card" ref={cardRef}>
        <div className="card-header">
          <h2>Request detail</h2>
        </div>
        <div className="detail">
          <Skeleton height={96} />
          <Skeleton height={140} />
        </div>
      </section>
    );
  }

  if (!request) {
    return (
      <section className="card" ref={cardRef}>
        <div className="card-header">
          <h2>Request detail</h2>
        </div>
        <EmptyState title="Nothing selected" text="Pick a request on the left to see its status and audit trail." />
      </section>
    );
  }

  return (
    <section className="card" ref={cardRef}>
      <div className="card-header">
        <h2>Request #{request.id}</h2>
        <StatusPill status={request.status} />
        <RiskBadge show={request.isHighRisk} />
        <div className="spacer" />
        <Button onClick={onRefresh} disabled={loading}>
          {loading ? 'Refreshing…' : 'Refresh'}
        </Button>
      </div>

      <div className="detail">
        {notice && <Banner notice={notice} />}

        <div className="detail-head">
          <div>
            <h2>
              {request.application.name} · {request.environment} · {request.accessLevel}
            </h2>
            <div className="sub">
              Requested by {request.requester.displayName} ({request.requester.email}) · {formatTime(request.createdAtUtc)}
            </div>
          </div>
        </div>

        <DetailFacts request={request} />

        <div>
          <div className="sub" style={{ color: 'var(--muted)', fontSize: 12, marginBottom: 6 }}>
            Justification
          </div>
          <p className="justification">{request.justification}</p>
        </div>

        {request.allowedActions.canApprove && !rejecting && (
          <div className="actions">
            <Button variant="primary" onClick={onApprove} disabled={busy}>
              {busy ? 'Working…' : 'Approve'}
            </Button>
            <Button variant="danger" onClick={() => setRejecting(true)} disabled={busy}>
              Reject
            </Button>
            <span className="hint">Your action is checked again on the server.</span>
          </div>
        )}

        {rejecting && (
          <RejectForm
            busy={busy}
            onSubmit={(reason) => {
              setRejecting(false);
              onReject(reason);
            }}
            onCancel={() => setRejecting(false)}
          />
        )}

        <div>
          <div style={{ fontSize: 12, color: 'var(--muted)', marginBottom: 10 }}>
            Audit trail — append-only, one entry per accepted transition
          </div>
          <ul className="timeline" ref={timelineRef}>
            {(request.audit ?? []).map((event) => (
              <AuditTimelineItem key={event.id} event={event} />
            ))}
          </ul>
        </div>
      </div>
    </section>
  );
}
