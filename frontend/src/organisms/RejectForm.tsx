import { useRef, useState } from 'react';
import { Button } from '../atoms/Button';
import { Field, TextArea } from '../atoms/Field';
import { useFadeIn } from '../lib/motion';

type Props = {
  busy: boolean;
  onSubmit: (reason: string) => void;
  onCancel: () => void;
};

/** A rejection without a reason is useless to the requester, so the client refuses it up front
 * (and the server refuses it again: this is UX, not the rule). */
export function RejectForm({ busy, onSubmit, onCancel }: Props) {
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const formRef = useRef<HTMLDivElement>(null);
  useFadeIn(formRef, null);

  function submit() {
    if (reason.trim().length === 0) {
      setError('A reason is required when rejecting.');
      return;
    }

    setError(null);
    onSubmit(reason.trim());
  }

  return (
    <div ref={formRef}>
      <Field
        label="Rejection reason (required)"
        htmlFor="reject-reason"
        error={error ?? undefined}
      >
        <TextArea
          id="reject-reason"
          rows={3}
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          placeholder="Tell the requester what they should do instead."
        />
      </Field>

      <div className="actions">
        <Button variant="danger" onClick={submit} disabled={busy}>
          Confirm rejection
        </Button>
        <Button onClick={onCancel} disabled={busy}>
          Cancel
        </Button>
      </div>
    </div>
  );
}
