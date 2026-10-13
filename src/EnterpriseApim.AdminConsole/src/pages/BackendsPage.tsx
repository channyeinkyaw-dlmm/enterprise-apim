import { useId, useState, type FormEvent } from 'react';
import { api, ApiError, type Cluster, type ClusterInput, type Destination } from '../api';
import {
  Checkbox,
  ConfirmDialog,
  Drawer,
  EmptyState,
  ErrorBanner,
  Field,
  Loading,
  PageHeader,
  useToast,
} from '../components';
import { useLoad } from '../hooks';
import { toApiError } from './ClientForm';

const POLICIES: { value: string; label: string }[] = [
  { value: '', label: 'Default (power of two choices)' },
  { value: 'RoundRobin', label: 'Round robin' },
  { value: 'LeastRequests', label: 'Least requests' },
  { value: 'Random', label: 'Random' },
  { value: 'FirstAlphabetical', label: 'First available, by name (failover)' },
];

function policyLabel(value: string | null) {
  return POLICIES.find((p) => p.value === (value ?? ''))?.label ?? value ?? '';
}

export function BackendsPage() {
  const notify = useToast();
  const clusters = useLoad(() => api.clusters.list(), []);
  const [editing, setEditing] = useState<Cluster | 'new' | null>(null);
  const [deleting, setDeleting] = useState<Cluster | null>(null);

  const addButton = (
    <button type="button" className="button button--primary" onClick={() => setEditing('new')}>
      Add backend
    </button>
  );

  return (
    <>
      <PageHeader
        title="Backends"
        lead="Your internal services. Each backend is one or more servers that routes send traffic to."
        actions={clusters.data?.length ? addButton : undefined}
      />

      <ErrorBanner message={clusters.error} />
      {clusters.loading && !clusters.data && <Loading />}

      {clusters.data && clusters.data.length === 0 && (
        <EmptyState title="No backends yet">
          <p>Add the service that should receive traffic, then create a route to it.</p>
          {addButton}
        </EmptyState>
      )}

      {clusters.data && clusters.data.length > 0 && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Backend</th>
                <th>Servers</th>
                <th>Load balancing</th>
                <th>Health check</th>
                <th className="num">Routes</th>
                <th>
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {clusters.data.map((c) => (
                <tr key={c.id}>
                  <td>
                    <code>{c.id}</code>
                  </td>
                  <td>
                    <ul className="server-list">
                      {c.destinations.map((d) => (
                        <li key={d.name}>
                          <span className="muted">{d.name}</span> <code>{d.address}</code>
                        </li>
                      ))}
                    </ul>
                  </td>
                  <td>{policyLabel(c.loadBalancingPolicy)}</td>
                  <td>
                    {c.healthCheckEnabled ? (
                      <>
                        Every {c.healthCheckIntervalSeconds ?? 15}s on <code>{c.healthCheckPath ?? '/health'}</code>
                      </>
                    ) : (
                      <span className="muted">Off</span>
                    )}
                  </td>
                  <td className="num">{c.routeCount}</td>
                  <td className="actions">
                    <button type="button" className="button button--small" onClick={() => setEditing(c)}>
                      Edit
                    </button>
                    <button
                      type="button"
                      className="button button--small button--danger-quiet"
                      onClick={() => setDeleting(c)}
                      disabled={c.routeCount > 0}
                      title={c.routeCount > 0 ? 'Routes still use this backend' : undefined}
                    >
                      Delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {editing && (
        <BackendDrawer
          initial={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
          onSaved={(cluster, created) => {
            setEditing(null);
            clusters.reload();
            notify(created ? `Added backend ${cluster.id}` : `Saved backend ${cluster.id}`);
          }}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title={`Delete backend ${deleting.id}?`}
          confirmLabel="Delete backend"
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            await api.clusters.remove(deleting.id);
            setDeleting(null);
            clusters.reload();
            notify(`Deleted backend ${deleting.id}`);
          }}
        >
          <p>No routes use it, so no traffic is affected.</p>
        </ConfirmDialog>
      )}
    </>
  );
}

function BackendDrawer(props: {
  initial?: Cluster;
  onSaved: (cluster: Cluster, created: boolean) => void;
  onClose: () => void;
}) {
  const { initial } = props;
  const formId = useId();
  const [id, setId] = useState(initial?.id ?? '');
  const [servers, setServers] = useState<Destination[]>(
    initial?.destinations.length ? initial.destinations : [{ name: 'primary', address: '' }],
  );
  const [policy, setPolicy] = useState(initial?.loadBalancingPolicy ?? '');
  const [healthEnabled, setHealthEnabled] = useState(initial?.healthCheckEnabled ?? true);
  const [healthPath, setHealthPath] = useState(initial?.healthCheckPath ?? '/health');
  const [healthInterval, setHealthInterval] = useState(initial?.healthCheckIntervalSeconds?.toString() ?? '15');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const updateServer = (index: number, patch: Partial<Destination>) =>
    setServers((list) => list.map((s, i) => (i === index ? { ...s, ...patch } : s)));

  const save = async (e: FormEvent) => {
    e.preventDefault();
    const interval = healthInterval.trim() ? Number(healthInterval) : null;

    const input: ClusterInput = {
      id: id.trim(),
      loadBalancingPolicy: policy || null,
      healthCheckEnabled: healthEnabled,
      healthCheckPath: healthEnabled ? healthPath.trim() || null : null,
      healthCheckIntervalSeconds: healthEnabled && interval !== null && Number.isFinite(interval) ? interval : null,
      destinations: servers.map((s) => ({ name: s.name.trim(), address: s.address.trim() })),
    };

    setBusy(true);
    setError(null);
    try {
      const saved = initial ? await api.clusters.update(initial.id, input) : await api.clusters.create(input);
      props.onSaved(saved, !initial);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  };

  const fieldErrors = error?.fieldErrors ?? {};

  return (
    <Drawer
      title={initial ? `Edit backend ${initial.id}` : 'Add a backend'}
      onClose={props.onClose}
      footer={
        <>
          <button type="button" className="button button--quiet" onClick={props.onClose}>
            Cancel
          </button>
          <button type="submit" form={formId} className="button button--primary" disabled={busy}>
            {busy ? 'Saving…' : initial ? 'Save backend' : 'Add backend'}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={save} noValidate className="form">
        {!initial && (
          <Field label="Backend ID" hint="Lowercase letters, digits and dashes. Can't be changed later." errors={fieldErrors.id}>
            {(fid, d) => (
              <input
                id={fid}
                className="mono"
                value={id}
                placeholder="orders-service"
                onChange={(e) => setId(e.target.value)}
                aria-describedby={d}
                spellCheck={false}
              />
            )}
          </Field>
        )}

        <fieldset className="fieldset">
          <legend>Servers</legend>
          <div className="server-editor">
            {servers.map((server, index) => (
              <div className="server-editor__row" key={index}>
                <input
                  aria-label={`Server ${index + 1} name`}
                  className="mono server-editor__name"
                  value={server.name}
                  placeholder="primary"
                  onChange={(e) => updateServer(index, { name: e.target.value })}
                  spellCheck={false}
                />
                <input
                  aria-label={`Server ${index + 1} address`}
                  className="mono"
                  value={server.address}
                  placeholder="http://orders-service:8080/"
                  onChange={(e) => updateServer(index, { address: e.target.value })}
                  spellCheck={false}
                />
                <button
                  type="button"
                  className="icon-button"
                  aria-label={`Remove server ${index + 1}`}
                  onClick={() => setServers((list) => list.filter((_, i) => i !== index))}
                  disabled={servers.length === 1}
                >
                  ×
                </button>
              </div>
            ))}
          </div>
          <button
            type="button"
            className="button button--small"
            onClick={() => setServers((list) => [...list, { name: `server-${list.length + 1}`, address: '' }])}
          >
            Add server
          </button>
          {fieldErrors.destinations && <p className="field__error">{fieldErrors.destinations.join(' ')}</p>}
        </fieldset>

        <Field label="Load balancing" hint="How requests are spread when there's more than one server." errors={fieldErrors.loadBalancingPolicy}>
          {(fid, d) => (
            <select id={fid} value={policy} onChange={(e) => setPolicy(e.target.value)} aria-describedby={d}>
              {POLICIES.map((p) => (
                <option key={p.value} value={p.value}>
                  {p.label}
                </option>
              ))}
            </select>
          )}
        </Field>

        <Checkbox
          label="Check server health"
          hint="Servers that fail the check stop receiving traffic until they recover."
          checked={healthEnabled}
          onChange={setHealthEnabled}
        />

        {healthEnabled && (
          <div className="field-row">
            <Field label="Health path" errors={fieldErrors.healthCheckPath}>
              {(fid, d) => (
                <input id={fid} className="mono" value={healthPath} onChange={(e) => setHealthPath(e.target.value)} aria-describedby={d} />
              )}
            </Field>
            <Field label="Every (seconds)" errors={fieldErrors.healthCheckIntervalSeconds}>
              {(fid, d) => (
                <input
                  id={fid}
                  inputMode="numeric"
                  value={healthInterval}
                  onChange={(e) => setHealthInterval(e.target.value)}
                  aria-describedby={d}
                />
              )}
            </Field>
          </div>
        )}

        <ErrorBanner message={Object.keys(fieldErrors).length ? null : error?.message} />
      </form>
    </Drawer>
  );
}
