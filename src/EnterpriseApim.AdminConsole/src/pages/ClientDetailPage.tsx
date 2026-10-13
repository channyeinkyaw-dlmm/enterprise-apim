import { useState } from 'react';
import { api, type ApiKey, type CreatedApiKey } from '../api';
import {
  ConfirmDialog,
  EmptyState,
  ErrorBanner,
  KeyStatusPill,
  Loading,
  Modal,
  PageHeader,
  Pill,
  useToast,
} from '../components';
import { formatDate, formatDateTime, timeAgo } from '../format';
import { navigate, useLoad } from '../hooks';
import { KeySlip } from '../KeySlip';
import { ClientFormDrawer } from './ClientForm';
import { KeyDrawer } from './KeyDrawer';

export function ClientDetailPage({ id }: { id: string }) {
  const notify = useToast();
  const tenant = useLoad(() => api.tenants.get(id), [id]);
  const keys = useLoad(() => api.keys.list(id), [id]);

  const [editing, setEditing] = useState(false);
  const [issuing, setIssuing] = useState(false);
  const [editingKey, setEditingKey] = useState<ApiKey | null>(null);
  const [issued, setIssued] = useState<CreatedApiKey | null>(null);
  const [revoking, setRevoking] = useState<ApiKey | null>(null);
  const [deleting, setDeleting] = useState(false);

  const back = <a href="#/clients">All clients</a>;

  if (tenant.loading && !tenant.data) return <Loading />;
  if (!tenant.data) {
    return (
      <>
        <PageHeader back={back} title="Client not found" />
        <ErrorBanner message={tenant.error} />
      </>
    );
  }

  const t = tenant.data;
  const activeKeys = keys.data?.filter((k) => k.status === 'active').length ?? 0;

  return (
    <>
      <PageHeader
        back={back}
        title={t.name}
        lead={t.description ?? undefined}
        actions={
          <button type="button" className="button" onClick={() => setEditing(true)}>
            Edit client
          </button>
        }
      />

      <dl className="facts">
        <div>
          <dt>Status</dt>
          <dd>{t.isActive ? <Pill tone="ok">Can call APIs</Pill> : <Pill tone="danger">Blocked</Pill>}</dd>
        </div>
        <div>
          <dt>Rate limit</dt>
          <dd>{t.rateLimitPerMinute ? `${t.rateLimitPerMinute.toLocaleString()} requests / min` : 'No limit'}</dd>
        </div>
        <div>
          <dt>Allowed IPs</dt>
          <dd>
            {t.allowedIpRanges.length === 0 ? (
              'Any IP'
            ) : (
              <span className="mono-list">
                {t.allowedIpRanges.map((r) => (
                  <code key={r}>{r}</code>
                ))}
              </span>
            )}
          </dd>
        </div>
        <div>
          <dt>Client ID</dt>
          <dd>
            <code className="select-all">{t.id}</code>
          </dd>
        </div>
      </dl>

      <section className="section">
        <div className="section__head">
          <div>
            <h2>Access keys</h2>
            <p className="section__lead">
              Rotate without downtime: issue a new key, let the client switch, then revoke the old one.
            </p>
          </div>
          <button type="button" className="button button--primary" onClick={() => setIssuing(true)}>
            Issue key
          </button>
        </div>

        <ErrorBanner message={keys.error} />
        {keys.loading && !keys.data && <Loading />}

        {keys.data && keys.data.length === 0 && (
          <EmptyState title="No keys issued">
            <p>This client can't call any API until it has a key.</p>
          </EmptyState>
        )}

        {keys.data && keys.data.length > 0 && (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Key ID</th>
                  <th>Scopes</th>
                  <th>Status</th>
                  <th>Last used</th>
                  <th>Expires</th>
                  <th>
                    <span className="visually-hidden">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {keys.data.map((k) => (
                  <tr key={k.id} className={k.status === 'active' ? undefined : 'row--inactive'}>
                    <td>
                      {k.name}
                      <span className="cell-note">Issued {formatDate(k.createdAt)}</span>
                    </td>
                    <td>
                      <code>{k.keyId}</code>
                    </td>
                    <td>
                      {k.scopes.length === 0 ? (
                        <span className="muted">None</span>
                      ) : (
                        <span className="mono-list">
                          {k.scopes.map((s) => (
                            <code key={s}>{s}</code>
                          ))}
                        </span>
                      )}
                    </td>
                    <td>
                      <KeyStatusPill status={k.status} />
                    </td>
                    <td title={formatDateTime(k.lastUsedAt)}>
                      {k.lastUsedAt ? timeAgo(k.lastUsedAt) : <span className="muted">Never</span>}
                    </td>
                    <td>{k.expiresAt ? formatDate(k.expiresAt) : <span className="muted">Never</span>}</td>
                    <td className="actions">
                      {k.status !== 'revoked' && (
                        <button
                          type="button"
                          className="button button--small"
                          onClick={() => setEditingKey(k)}
                          aria-label={`Edit key ${k.name}`}
                        >
                          Edit
                        </button>
                      )}
                      {k.status === 'active' && (
                        <button type="button" className="button button--small button--danger-quiet" onClick={() => setRevoking(k)}>
                          Revoke
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="section section--danger">
        <h2>Delete client</h2>
        <p>
          Deletes {t.name} and all of its keys. Their requests are rejected immediately. To pause access
          instead, edit the client and turn off "Client can call APIs".
        </p>
        <button type="button" className="button button--danger" onClick={() => setDeleting(true)}>
          Delete client
        </button>
      </section>

      {editing && (
        <ClientFormDrawer
          initial={t}
          onClose={() => setEditing(false)}
          onSaved={(saved) => {
            setEditing(false);
            tenant.reload();
            notify(`Saved ${saved.name}`);
          }}
        />
      )}

      {editingKey && (
        <KeyDrawer
          tenant={t}
          initial={editingKey}
          onClose={() => setEditingKey(null)}
          onSaved={(key) => {
            setEditingKey(null);
            keys.reload();
            notify(`Saved key ${key.name}`);
          }}
        />
      )}

      {issuing && (
        <KeyDrawer
          tenant={t}
          onClose={() => setIssuing(false)}
          onIssued={(created) => {
            setIssuing(false);
            setIssued(created);
            keys.reload();
          }}
        />
      )}

      {issued && (
        <Modal
          wide
          title="Copy the new key now"
          footer={
            <button type="button" className="button" onClick={() => setIssued(null)}>
              Done, I've stored it
            </button>
          }
        >
          <p>
            Send it to {t.name} over a secure channel. Only a hash is stored, so this is the only time the
            full key can be shown.
          </p>
          <KeySlip plainKey={issued.plainKey} />
        </Modal>
      )}

      {revoking && (
        <ConfirmDialog
          title={`Revoke "${revoking.name}"?`}
          confirmLabel="Revoke key"
          onClose={() => setRevoking(null)}
          onConfirm={async () => {
            await api.keys.revoke(t.id, revoking.keyId);
            setRevoking(null);
            keys.reload();
            notify('Key revoked. Gateways reject it from now on.');
          }}
        >
          <p>
            Requests using <code>{revoking.keyId}</code> are rejected within seconds on every gateway.
            {activeKeys <= 1 && ` This is ${t.name}'s last active key, so its integration will stop working.`}
          </p>
        </ConfirmDialog>
      )}

      {deleting && (
        <ConfirmDialog
          title={`Delete ${t.name}?`}
          confirmLabel="Delete client"
          onClose={() => setDeleting(false)}
          onConfirm={async () => {
            await api.tenants.remove(t.id);
            notify(`Deleted ${t.name}`);
            navigate('/clients');
          }}
        >
          <p>
            Its {keys.data?.length ?? 0} key(s) are deleted too, and its requests are rejected immediately.
            This can't be undone.
          </p>
        </ConfirmDialog>
      )}
    </>
  );
}
