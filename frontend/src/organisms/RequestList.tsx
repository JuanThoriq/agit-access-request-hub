import { useRef } from 'react';
import { Skeleton } from '../atoms/Skeleton';
import { EmptyState } from '../molecules/EmptyState';
import { RequestRow } from '../molecules/RequestRow';
import { useStaggerIn } from '../lib/motion';
import type { AccessRequest } from '../lib/types';

type Props = {
  requests: AccessRequest[];
  loading: boolean;
  selectedId: number | null;
  showRequester: boolean;
  emptyTitle: string;
  emptyText: string;
  animationKey: unknown;
  onSelect: (id: number) => void;
};

export function RequestList({
  requests,
  loading,
  selectedId,
  showRequester,
  emptyTitle,
  emptyText,
  animationKey,
  onSelect,
}: Props) {
  const listRef = useRef<HTMLUListElement>(null);
  useStaggerIn(listRef, 'li', animationKey);

  if (loading) {
    return (
      <div style={{ padding: 6 }}>
        <Skeleton />
        <Skeleton />
        <Skeleton />
      </div>
    );
  }

  if (requests.length === 0) {
    return <EmptyState title={emptyTitle} text={emptyText} />;
  }

  return (
    <ul className="list" ref={listRef}>
      {requests.map((request) => (
        <li key={request.id}>
          <RequestRow
            request={request}
            selected={selectedId === request.id}
            showRequester={showRequester}
            onSelect={onSelect}
          />
        </li>
      ))}
    </ul>
  );
}
