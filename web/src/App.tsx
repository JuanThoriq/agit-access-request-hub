import { useCallback, useEffect, useState } from 'react';
import { ApiError, IDENTITY_STORAGE_KEY, api } from './api';
import { CreateRequestForm } from './components/CreateRequestForm';
import { RequestDetail } from './components/RequestDetail';
import {
  DEMO_USERS,
  STATUS_LABEL,
  type AccessRequest,
  type ApplicationRef,
  type CurrentUser,
} from './types';

type Tab = 'mine' | 'inbox' | 'all';

type Notice = { kind: 'ok' | 'warn' | 'error'; text: string; code?: string };

const formatTime = (iso: string) => new Date(iso).toLocaleString();

export default function App() {
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

  const describe = (error: unknown): Notice =>
    error instanceof ApiError
      ? { kind: 'error', text: error.message, code: error.code }
      : { kind: 'error', text: 'Could not reach the API. Is it running on port 5080?', code: 'network_error' };

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
        if (!currentUser.isAdmin && tab === 'all') setTab('mine');
      })
      .catch((error) => !cancelled && setPageError(describe(error)));

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
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
      const updated = action === 'approve'
        ? await api.approve(detail.id, detail.version)
        : await api.reject(detail.id, detail.version, reason);

      setDetail(updated);
      setNotice({
        kind: 'ok',
        text: `Request #${updated.id} is now ${STATUS_LABEL[updated.status]}.`,
      });
      await loadList();
    } catch (error) {
      if (error instanceof ApiError && (error.code === 'version_conflict' || error.code === 'invalid_transition')) {
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
        me.ownedApplications.length > 0 ? `System owner: ${me.ownedApplications.map((a) => a.name).join(', ')}` : null,
        me.directReports.length > 0 ? `Manager of ${me.directReports.length}` : null,
      ]
        .filter(Boolean)
        .join(' · ')
    : '';

  return (
    <>
      <header className="app-header">
        <div className="brand">
          <h1>Access Request Hub</h1>
          <span>policy v1</span>
        </div>

        <div className="header-spacer" />

        <div className="identity">
          <span className="role">{roleSummary}</span>
          <select
            aria-label="Demo user"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          >
            {DEMO_USERS.map((user) => (
              <option key={user.email} value={user.email}>
                {user.label}
              </option>
            ))}
          </select>
        </div>
      </header>

      <main>
        <section className="card">
          <div className="tabs" role="tablist">
            <button
              type="button"
              role="tab"
              className="tab"
              aria-selected={tab === 'mine'}
              onClick={() => setTab('mine')}
            >
              My requests
            </button>
            <button
              type="button"
              role="tab"
              className="tab"
              aria-selected={tab === 'inbox'}
              onClick={() => setTab('inbox')}
            >
              Inbox
              {inboxCount > 0 && <span className="count">{inboxCount}</span>}
            </button>
            {me?.isAdmin && (
              <button
                type="button"
                role="tab"
                className="tab"
                aria-selected={tab === 'all'}
                onClick={() => setTab('all')}
              >
                All (audit)
              </button>
            )}
          </div>

          <div className="card-header">
            <h2>
              {tab === 'mine' ? 'My requests' : tab === 'inbox' ? 'Waiting on you' : 'All requests'}
            </h2>
            <div className="spacer" />
            <button type="button" className="btn primary" onClick={() => setShowCreate(true)}>
              New request
            </button>
          </div>

          {pageError && (
            <div style={{ padding: 12 }}>
              <div className={`banner ${pageError.kind}`}>
                <div>
                  {pageError.text}
                  {pageError.code && <div className="code">{pageError.code}</div>}
                </div>
              </div>
            </div>
          )}

          {listLoading ? (
            <div style={{ padding: 6 }}>
              <div className="skeleton" />
              <div className="skeleton" />
              <div className="skeleton" />
            </div>
          ) : requests.length === 0 ? (
            <div className="empty">
              <strong>
                {tab === 'mine'
                  ? 'You have no requests yet'
                  : tab === 'inbox'
                    ? 'Nothing is waiting on you'
                    : 'No requests in the system'}
              </strong>
              {tab === 'inbox'
                ? 'Requests appear here only while your own approval step is pending.'
                : 'Create one with the “New request” button.'}
            </div>
          ) : (
            <ul className="list">
              {requests.map((request) => (
                <li key={request.id}>
                  <button
                    type="button"
                    className="row"
                    aria-current={selectedId === request.id}
                    onClick={() => select(request.id)}
                  >
                    <span className="row-top">
                      <span className="row-title">
                        #{request.id} · {request.application.name}
                      </span>
                      <span className="pill" data-status={request.status}>
                        {STATUS_LABEL[request.status]}
                      </span>
                      {request.isHighRisk && <span className="badge risk">high risk</span>}
                    </span>
                    <span className="row-meta">
                      <span>{request.environment}</span>
                      <span className="dot">·</span>
                      <span>{request.accessLevel}</span>
                      {tab !== 'mine' && (
                        <>
                          <span className="dot">·</span>
                          <span>{request.requester.displayName}</span>
                        </>
                      )}
                      <span className="dot">·</span>
                      <span>v{request.version}</span>
                      <span className="dot">·</span>
                      <span>{formatTime(request.updatedAtUtc)}</span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
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
      </main>

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
    </>
  );
}
