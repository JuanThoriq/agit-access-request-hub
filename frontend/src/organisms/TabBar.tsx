import { Button } from '../atoms/Button';

export type Tab = 'mine' | 'inbox' | 'all';

type Props = {
  tab: Tab;
  inboxCount: number;
  isAdmin: boolean;
  onChange: (tab: Tab) => void;
};

export function TabBar({ tab, inboxCount, isAdmin, onChange }: Props) {
  return (
    <div className="tabs" role="tablist">
      <Button role="tab" className="tab" aria-selected={tab === 'mine'} onClick={() => onChange('mine')}>
        My requests
      </Button>
      <Button role="tab" className="tab" aria-selected={tab === 'inbox'} onClick={() => onChange('inbox')}>
        Inbox
        {inboxCount > 0 && <span className="count">{inboxCount}</span>}
      </Button>
      {isAdmin && (
        <Button role="tab" className="tab" aria-selected={tab === 'all'} onClick={() => onChange('all')}>
          All (audit)
        </Button>
      )}
    </div>
  );
}
