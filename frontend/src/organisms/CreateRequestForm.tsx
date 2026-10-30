import { useEffect, useRef, useState } from 'react';
import { Banner } from '../atoms/Banner';
import { Button } from '../atoms/Button';
import { Field, Select, TextArea } from '../atoms/Field';
import { ApiError, api } from '../lib/api';
import { usePopIn } from '../lib/motion';
import type { ApplicationRef, CreateRequestInput } from '../lib/types';

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

  const panelRef = useRef<HTMLDivElement>(null);
  usePopIn(panelRef);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
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
      <div className="modal" ref={panelRef} onClick={(event) => event.stopPropagation()}>
        <div className="card-header">
          <h2 id="create-title">New access request</h2>
          <div className="spacer" />
          <Button onClick={onClose}>Close</Button>
        </div>

        <form onSubmit={submit}>
          {formError && <Banner notice={{ kind: 'error', text: formError.detail, code: formError.code }} />}

          <div className="grid-2">
            <Field label="Application" htmlFor="application">
              <Select
                id="application"
                value={form.applicationId}
                onChange={(event) => setForm({ ...form, applicationId: Number(event.target.value) })}
              >
                {applications.map((application) => (
                  <option key={application.id} value={application.id}>
                    {application.name} — owner {application.systemOwner.displayName}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label="Environment" htmlFor="environment">
              <Select
                id="environment"
                value={form.environment}
                onChange={(event) =>
                  setForm({ ...form, environment: event.target.value as CreateRequestInput['environment'] })
                }
              >
                <option value="NonProduction">NonProduction</option>
                <option value="Production">Production</option>
              </Select>
            </Field>
          </div>

          <Field
            label="Access level"
            htmlFor="accessLevel"
            hint={
              form.environment === 'Production' || form.accessLevel === 'Admin'
                ? 'High-risk request: after your manager, the system owner must also approve.'
                : undefined
            }
          >
            <Select
              id="accessLevel"
              value={form.accessLevel}
              onChange={(event) =>
                setForm({ ...form, accessLevel: event.target.value as CreateRequestInput['accessLevel'] })
              }
            >
              <option value="Read">Read</option>
              <option value="Admin">Admin</option>
            </Select>
          </Field>

          <Field
            label="Justification"
            htmlFor="justification"
            hint="At least 10 characters. Stored with the request and shown to every approver."
            error={errorsFor('justification')}
          >
            <TextArea
              id="justification"
              rows={4}
              value={form.justification}
              onChange={(event) => setForm({ ...form, justification: event.target.value })}
              placeholder="Why do you need this access, and for how long?"
            />
          </Field>

          <div className="modal-footer">
            <span className="hint" style={{ marginRight: 'auto' }}>
              idempotency key: <code>{clientRequestId.slice(0, 8)}…</code>
            </span>
            <Button onClick={onClose} disabled={submitting}>
              Cancel
            </Button>
            <Button type="submit" variant="primary" disabled={submitting}>
              {submitting ? 'Submitting…' : 'Submit request'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
