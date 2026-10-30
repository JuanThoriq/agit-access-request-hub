import { DEMO_USERS } from '../lib/types';

type Props = {
  email: string;
  roleSummary: string;
  onChange: (email: string) => void;
};

/**
 * The demo identity travels in the X-Demo-User header. Switching it only changes *who you are*:
 * the server resolves it to a seeded user and re-checks every rule on every call.
 */
export function IdentityPicker({ email, roleSummary, onChange }: Props) {
  return (
    <div className="identity">
      <span className="role">{roleSummary}</span>
      <select aria-label="Demo user" value={email} onChange={(event) => onChange(event.target.value)}>
        {DEMO_USERS.map((user) => (
          <option key={user.email} value={user.email}>
            {user.label}
          </option>
        ))}
      </select>
    </div>
  );
}
