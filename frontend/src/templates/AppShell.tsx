import type { ReactNode } from 'react';

/** Page skeleton: header on top, one two-column area under it. */
export function AppShell({ header, children }: { header: ReactNode; children: ReactNode }) {
  return (
    <>
      {header}
      <main>{children}</main>
    </>
  );
}
