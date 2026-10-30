import { formatTime } from '../lib/format';
import { EVENT_LABEL, STATUS_LABEL, type AuditEvent, type RequestStatus } from '../lib/types';

export function AuditTimelineItem({ event }: { event: AuditEvent }) {
  return (
    <li>
      <div className="event-title">
        {EVENT_LABEL[event.eventType] ?? event.eventType}
        {event.fromStatus && (
          <span className="event-meta">
            {' '}
            · {STATUS_LABEL[event.fromStatus as RequestStatus]} → {STATUS_LABEL[event.toStatus as RequestStatus]}
          </span>
        )}
      </div>
      <div className="event-meta">
        {event.actor.displayName} · {formatTime(event.atUtc)}
      </div>
      {event.reason && <div className="event-reason">{event.reason}</div>}
    </li>
  );
}
