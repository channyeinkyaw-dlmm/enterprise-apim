import { useState } from 'react';
import { api, type Tenant } from '../api';
import { EmptyState, ErrorBanner, Loading, PageHeader, Pill, useToast } from '../components';
import { formatDate } from '../format';
import { navigate, useLoad } from '../hooks';
import { ClientFormDrawer } from './ClientForm';

export function ClientsPage() {
  const notify = useToast();
  const tenants = useLoad(() => api.tenants.list(), []);
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<Tenant | null>(null);

  const addButton = (
    <button type="button" className="button button--primary" onClick={() => setAdding(true)}>
      Add client
    </button>
  );

  return (
    <>
      <PageHeader
        title="Clients"
        lead="Partner organisations allowed to call APIs through the gateway. Each one gets its own access keys."
        actions={tenants.data?.length ? addButton : undefined}
      />

      <ErrorBanner message={tenants.error} />
      {tenants.loading && !tenants.data && <Loading />}

      {tenants.data && tenants.data.length === 0 && (
        <EmptyState title="No clients yet">
          <p>Add the first partner organisation, then issue it an access key.</p>
          {addButton}
        </EmptyState>
      )}

      {tenants.data && tenants.data.length > 0 && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Client</th>
                <th>Status</th>
                <th className="num">Active keys</th>
                <th>Rate limit</th>
                <th>IP restriction</th>
                <th>Added</th>
                <th>
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {tenants.data.map((t) => (
                <tr key={t.id}>
                  <td>
                    <a className="row-link" href={`#/clients/${t.id}`}>
                      {t.name}
                    </a>
                    {t.description && <span className="cell-note">{t.description}</span>}
                  </td>
                  <td data-label="Status">{t.isActive ? <Pill tone="ok">Can call APIs</Pill> : <Pill tone="danger">Blocked</Pill>}</td>
                  <td data-label="Active keys" className="num">{t.activeKeyCount}</td>
                  <td data-label="Rate limit">{t.rateLimitPerMinute ? `${t.rateLimitPerMinute.toLocaleString()} / min` : 'No limit'}</td>
                  <td data-label="IP restriction">
                    {t.allowedIpRanges.length === 0
                      ? 'Any IP'
                      : `${t.allowedIpRanges.length} range${t.allowedIpRanges.length === 1 ? '' : 's'}`}
                  </td>
                  <td data-label="Added">{formatDate(t.createdAt)}</td>
                  <td className="actions">
                    <button
                      type="button"
                      className="button button--small"
                      onClick={() => setEditing(t)}
                      aria-label={`Edit ${t.name}`}
                    >
                      Edit
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {editing && (
        <ClientFormDrawer
          initial={editing}
          onClose={() => setEditing(null)}
          onSaved={(tenant) => {
            setEditing(null);
            notify(`Saved ${tenant.name}`);
            tenants.reload();
          }}
        />
      )}

      {adding && (
        <ClientFormDrawer
          onClose={() => setAdding(false)}
          onSaved={(tenant) => {
            setAdding(false);
            notify(`Added ${tenant.name}`);
            navigate(`/clients/${tenant.id}`);
          }}
        />
      )}
    </>
  );
}
