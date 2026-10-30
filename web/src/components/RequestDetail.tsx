import { useState } from 'react';
import { EVENT_LABEL, STATUS_LABEL, type AccessRequest } from '../types';

type Notice = { kind: 'ok' | 'warn' | 'error'; text: string; code?: string };

type Props = {
  request: AccessRequest | null;
  loading: boolean;
  busy: boolean;
  notice: Notice | null;
  onApprove: () => void;
  onReject: (reason: string) => void;
  onRefresh: () => void;
};

const formatTime = (iso: string) => new Date(iso).toLocaleString();

export function RequestDetail({ request, loading, busy, notice, onApprove, onReject, onRefresh }: Props) {
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string | null>(null);

  if (loading && !request) {
    return (
      <section className="card">
        <div className="card-header">
          <h2>Request detail</h2>
        </div>
        <div className="detail">
          <div className="skeleton" style={{ height: 96 }} />
          <div className="skeleton" style={{ height: 140 }} />
        </div>
      </section>
    );
  }

  if (!request) {
    return (
      <section className="card">
        <div className="card-header">
          <h2>Request detail</h2>
        </div>
        <div className="empty">
          <strong>Nothing selected</strong>
          Pick a request on the left to see its status and audit trail.
        </div>
      </section>
    );
  }

  function submitReject() {
    if (reason.trim().length === 0) {
      setReasonError('A reason is required when rejecting.');
      return;
    }
    setReasonError(null);
    onReject(reason.trim());
  }

  return (
    <section className="card">
      <div className="card-header">
        <h2>Request #{request.id}</h2>
        <span className="pill" data-status={request.status}>
          {STATUS_LABEL[request.status]}
        </span>
        {request.isHighRisk && <span className="badge risk">high risk</span>}
        <div className="spacer" />
        <button type="button" className="btn" onClick={onRefresh} disabled={loading}>
          {loading ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      <div className="detail">
        {notice && (
          <div className={`banner ${notice.kind}`}>
            <div>
              {notice.text}
              {notice.code && <div className="code">{notice.code}</div>}
            </div>
          </div>
        )}

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

        <div>
          <div className="sub" style={{ color: 'var(--muted)', fontSize: 12, marginBottom: 6 }}>
            Justification
          </div>
          <p className="justification">{request.justification}</p>
        </div>

        {request.allowedActions.canApprove && !rejecting && (
          <div className="actions">
            <button type="button" className="btn primary" onClick={onApprove} disabled={busy}>
              {busy ? 'Working…' : 'Approve'}
            </button>
            <button type="button" className="btn danger" onClick={() => setRejecting(true)} disabled={busy}>
              Reject
            </button>
            <span className="hint">Your action is checked again on the server.</span>
          </div>
        )}

        {rejecting && (
          <div className="field">
            <label htmlFor="reject-reason">Rejection reason (required)</label>
            <textarea
              id="reject-reason"
              rows={3}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Tell the requester what they should do instead."
            />
            {reasonError && <span className="error">{reasonError}</span>}
            <div className="actions">
              <button type="button" className="btn danger" onClick={submitReject} disabled={busy}>
                Confirm rejection
              </button>
              <button
                type="button"
                className="btn"
                onClick={() => {
                  setRejecting(false);
                  setReasonError(null);
                }}
                disabled={busy}
              >
                Cancel
              </button>
            </div>
          </div>
        )}

        <div>
          <div style={{ fontSize: 12, color: 'var(--muted)', marginBottom: 10 }}>
            Audit trail — append-only, one entry per accepted transition
          </div>
          <ul className="timeline">
            {(request.audit ?? []).map((event) => (
              <li key={event.id}>
                <div className="event-title">
                  {EVENT_LABEL[event.eventType] ?? event.eventType}
                  {event.fromStatus && (
                    <span className="event-meta">
                      {' '}
                      · {STATUS_LABEL[event.fromStatus as AccessRequest['status']]} →{' '}
                      {STATUS_LABEL[event.toStatus as AccessRequest['status']]}
                    </span>
                  )}
                </div>
                <div className="event-meta">
                  {event.actor.displayName} · {formatTime(event.atUtc)}
                </div>
                {event.reason && <div className="event-reason">{event.reason}</div>}
              </li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  );
}
