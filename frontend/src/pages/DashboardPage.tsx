import { useCallback, useEffect, useState } from 'react';
import { Banner, type Notice } from '../atoms/Banner';
import { Button } from '../atoms/Button';
import { ApiError, IDENTITY_STORAGE_KEY, api } from '../lib/api';
import {
  DEMO_USERS,
  STATUS_LABEL,
  type AccessRequest,
  type ApplicationRef,
  type CurrentUser,
} from '../lib/types';
import { AppHeader } from '../organisms/AppHeader';
import { CreateRequestForm } from '../organisms/CreateRequestForm';
import { RequestDetail } from '../organisms/RequestDetail';
import { RequestList } from '../organisms/RequestList';
import { TabBar, type Tab } from '../organisms/TabBar';
import { AppShell } from '../templates/AppShell';

const describe = (error: unknown): Notice =>
  error instanceof ApiError
    ? { kind: 'error', text: error.message, code: error.code }
    : { kind: 'error', text: 'Could not reach the API. Is it running on port 5080?', code: 'network_error' };

/**
 * Owns the page state: who is logged in, which tab, which request is selected, what the last
 * action reported. Everything below is presentational.
 */
export function DashboardPage() {
  const [email, setEmail] = useState(() => localStorage.getItem(IDENTITY_STORAGE_KEY) ?? DEMO_USERS[0].email);
  const [me, setMe] = useState<CurrentUser | null>(null);
  const [applications, setApplications] = useState<ApplicationRef[]>([]);
  const [tab, setTab] = useState<Tab>('mine');

  const [requests, setRequests] = useState<AccessRequest[]>([]);
  const [listLoading, setListLoading] = useState(true);
  const [inboxCount, setInboxCount] = useState(0);

  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [detail, setDetail] = useState<AccessRequest | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [pageError, setPageError] = useState<Notice | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  // Identity changes reload everything: the same UI renders a different set of permissions.
  useEffect(() => {
    localStorage.setItem(IDENTITY_STORAGE_KEY, email);
    setSelectedId(null);
    setDetail(null);
    setNotice(null);
    setPageError(null);

    let cancelled = false;
    Promise.all([api.me(), api.applications()])
      .then(([currentUser, apps]) => {
        if (cancelled) return;
        setMe(currentUser);
        setApplications(apps);
        if (!currentUser.isAdmin) setTab((current) => (current === 'all' ? 'mine' : current));
      })
      .catch((error) => !cancelled && setPageError(describe(error)));

    return () => {
      cancelled = true;
    };
  }, [email]);

  const loadList = useCallback(async () => {
    setListLoading(true);
    setPageError(null);

    try {
      const [list, inbox] = await Promise.all([
        tab === 'mine' ? api.myRequests() : tab === 'inbox' ? api.inbox() : api.allRequests(),
        api.inbox(),
      ]);
      setRequests(list);
      setInboxCount(inbox.length);
    } catch (error) {
      setPageError(describe(error));
      setRequests([]);
    } finally {
      setListLoading(false);
    }
  }, [tab]);

  useEffect(() => {
    void loadList();
  }, [loadList, email]);

  const loadDetail = useCallback(async (id: number) => {
    setDetailLoading(true);
    try {
      setDetail(await api.detail(id));
      setPageError(null);
    } catch (error) {
      setDetail(null);
      setPageError(describe(error));
    } finally {
      setDetailLoading(false);
    }
  }, []);

  function select(id: number) {
    setSelectedId(id);
    setNotice(null);
    setDetail(null);
    void loadDetail(id);
  }

  async function act(action: 'approve' | 'reject', reason = '') {
    if (!detail) return;
    setBusy(true);
    setNotice(null);

    try {
      const updated =
        action === 'approve'
          ? await api.approve(detail.id, detail.version)
          : await api.reject(detail.id, detail.version, reason);

      setDetail(updated);
      setNotice({ kind: 'ok', text: `Request #${updated.id} is now ${STATUS_LABEL[updated.status]}.` });
      await loadList();
    } catch (error) {
      if (
        error instanceof ApiError &&
        (error.code === 'version_conflict' || error.code === 'invalid_transition')
      ) {
        // Someone else moved the request first. Never retry silently: show the new state, let the
        // user decide again against the fresh version.
        setNotice({
          kind: 'warn',
          text: 'This request changed while you were looking at it. The latest state is loaded below — review it and act again.',
          code: error.code,
        });
        await loadDetail(detail.id);
        await loadList();
      } else {
        setNotice(describe(error));
      }
    } finally {
      setBusy(false);
    }
  }

  const roleSummary = me
    ? [
        me.isAdmin ? 'Admin/Auditor (read-only, not an approver)' : null,
        me.ownedApplications.length > 0
          ? `System owner: ${me.ownedApplications.map((application) => application.name).join(', ')}`
          : null,
        me.directReports.length > 0 ? `Manager of ${me.directReports.length}` : null,
      ]
        .filter(Boolean)
        .join(' · ')
    : '';

  const heading = tab === 'mine' ? 'My requests' : tab === 'inbox' ? 'Waiting on you' : 'All requests';

  const emptyTitle =
    tab === 'mine'
      ? 'You have no requests yet'
      : tab === 'inbox'
        ? 'Nothing is waiting on you'
        : 'No requests in the system';

  const emptyText =
    tab === 'inbox'
      ? 'Requests appear here only while your own approval step is pending.'
      : 'Create one with the “New request” button.';

  return (
    <AppShell
      header={<AppHeader email={email} roleSummary={roleSummary} onIdentityChange={setEmail} />}
    >
      <section className="card">
        <TabBar tab={tab} inboxCount={inboxCount} isAdmin={me?.isAdmin ?? false} onChange={setTab} />

        <div className="card-header">
          <h2>{heading}</h2>
          <div className="spacer" />
          <Button variant="primary" onClick={() => setShowCreate(true)}>
            New request
          </Button>
        </div>

        {pageError && (
          <div style={{ padding: 12 }}>
            <Banner notice={pageError} />
          </div>
        )}

        <RequestList
          requests={requests}
          loading={listLoading}
          selectedId={selectedId}
          showRequester={tab !== 'mine'}
          emptyTitle={emptyTitle}
          emptyText={emptyText}
          animationKey={`${tab}:${email}:${requests.length}`}
          onSelect={select}
        />
      </section>

      <RequestDetail
        request={detail}
        loading={detailLoading}
        busy={busy}
        notice={notice}
        onApprove={() => void act('approve')}
        onReject={(reason) => void act('reject', reason)}
        onRefresh={() => selectedId !== null && void loadDetail(selectedId)}
      />

      {showCreate && (
        <CreateRequestForm
          applications={applications}
          onClose={() => setShowCreate(false)}
          onCreated={(id) => {
            setShowCreate(false);
            setTab('mine');
            setNotice({ kind: 'ok', text: `Request #${id} created and sent for manager approval.` });
            setSelectedId(id);
            void loadDetail(id);
            void loadList();
          }}
        />
      )}
    </AppShell>
  );
}
