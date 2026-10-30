import { IdentityPicker } from '../molecules/IdentityPicker';

type Props = {
  email: string;
  roleSummary: string;
  onIdentityChange: (email: string) => void;
};

export function AppHeader({ email, roleSummary, onIdentityChange }: Props) {
  return (
    <header className="app-header">
      <div className="brand">
        <h1>Access Request Hub</h1>
        <span>policy v1</span>
      </div>

      <div className="header-spacer" />

      <IdentityPicker email={email} roleSummary={roleSummary} onChange={onIdentityChange} />
    </header>
  );
}
