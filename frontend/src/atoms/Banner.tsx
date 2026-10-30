import { useRef } from 'react';
import { useSlideIn } from '../lib/motion';

export type Notice = { kind: 'ok' | 'warn' | 'error'; text: string; code?: string };

export function Banner({ notice }: { notice: Notice }) {
  const ref = useRef<HTMLDivElement>(null);
  useSlideIn(ref, notice.text);

  return (
    <div className={`banner ${notice.kind}`} ref={ref}>
      <div>
        {notice.text}
        {notice.code && <div className="code">{notice.code}</div>}
      </div>
    </div>
  );
}
