import { useEffect, useState } from 'react';
import { ApiError, api } from '../api';
import type { ApplicationRef, CreateRequestInput } from '../types';

type Props = {
  applications: ApplicationRef[];
  onClose: () => void;
  onCreated: (requestId: number) => void;
};

function newClientRequestId() {
  return crypto.randomUUID();
}

/**
 * The client request id is generated once per form and only replaced after a successful submit.
 * That is what makes a retry idempotent: pressing "Create" again after a timeout reuses the key,
 * so the backend returns the original request instead of storing a second one.
 */
export function CreateRequestForm({ applications, onClose, onCreated }: Props) {
  const [clientRequestId, setClientRequestId] = useState(newClientRequestId);
  const [form, setForm] = useState({
    applicationId: applications[0]?.id ?? 0,
    environment: 'NonProduction' as CreateRequestInput['environment'],
    accessLevel: 'Read' as CreateRequestInput['accessLevel'],
    justification: '',
  });
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
  const [formError, setFormError] = useState<{ code: string; detail: string } | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setFieldErrors({});
    setFormError(null);

    try {
      const created = await api.create({ clientRequestId, ...form });
      setClientRequestId(newClientRequestId());
      onCreated(created.id);
    } catch (error) {
      if (error instanceof ApiError) {
        setFieldErrors(error.fieldErrors);
        setFormError({ code: error.code, detail: error.message });
      } else {
        setFormError({ code: 'network_error', detail: 'Could not reach the API. Is it running on port 5080?' });
      }
    } finally {
      setSubmitting(false);
    }
  }

  const errorsFor = (field: string) => fieldErrors[field]?.[0];

  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="create-title" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="card-header">
          <h2 id="create-title">New access request</h2>
          <div className="spacer" />
          <button type="button" className="btn" onClick={onClose}>
            Close
          </button>
        </div>

        <form onSubmit={submit}>
          {formError && (
            <div className="banner error">
              <div>
                {formError.detail}
                <div className="code">{formError.code}</div>
              </div>
            </div>
          )}

          <div className="grid-2">
            <div className="field">
              <label htmlFor="application">Application</label>
              <select
                id="application"
                value={form.applicationId}
                onChange={(e) => setForm({ ...form, applicationId: Number(e.target.value) })}
              >
                {applications.map((application) => (
                  <option key={application.id} value={application.id}>
                    {application.name} — owner {application.systemOwner.displayName}
                  </option>
                ))}
              </select>
            </div>

            <div className="field">
              <label htmlFor="environment">Environment</label>
              <select
                id="environment"
                value={form.environment}
                onChange={(e) => setForm({ ...form, environment: e.target.value as CreateRequestInput['environment'] })}
              >
                <option value="NonProduction">NonProduction</option>
                <option value="Production">Production</option>
              </select>
            </div>
          </div>

          <div className="field">
            <label htmlFor="accessLevel">Access level</label>
            <select
              id="accessLevel"
              value={form.accessLevel}
              onChange={(e) => setForm({ ...form, accessLevel: e.target.value as CreateRequestInput['accessLevel'] })}
            >
              <option value="Read">Read</option>
              <option value="Admin">Admin</option>
            </select>
            {form.environment === 'Production' || form.accessLevel === 'Admin' ? (
              <span className="hint">High-risk request: after your manager, the system owner must also approve.</span>
            ) : null}
          </div>

          <div className="field">
            <label htmlFor="justification">Justification</label>
            <textarea
              id="justification"
              rows={4}
              value={form.justification}
              onChange={(e) => setForm({ ...form, justification: e.target.value })}
              placeholder="Why do you need this access, and for how long?"
            />
            <span className="hint">At least 10 characters. Stored with the request and shown to every approver.</span>
            {errorsFor('justification') && <span className="error">{errorsFor('justification')}</span>}
          </div>

          <div className="modal-footer">
            <span className="hint" style={{ marginRight: 'auto' }}>
              idempotency key: <code>{clientRequestId.slice(0, 8)}…</code>
            </span>
            <button type="button" className="btn" onClick={onClose} disabled={submitting}>
              Cancel
            </button>
            <button type="submit" className="btn primary" disabled={submitting}>
              {submitting ? 'Submitting…' : 'Submit request'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
