import { useId, useState, type FormEvent } from 'react';
import { api, ApiError, type Cluster, type Route, type RouteInput } from '../api';
import {
  Checkbox,
  ConfirmDialog,
  Drawer,
  EmptyState,
  ErrorBanner,
  Field,
  Loading,
  PageHeader,
  Pill,
  useToast,
} from '../components';
import { useLoad } from '../hooks';
import { toApiError } from './ClientForm';

const METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'];

type Access = 'client' | 'scope' | 'public';

function accessOf(route: Route): Access {
  if (route.allowAnonymous) return 'public';
  return route.requiredScope ? 'scope' : 'client';
}

export function AccessPill({ route }: { route: Route }) {
  switch (accessOf(route)) {
    case 'public':
      return <Pill tone="warn">Public, no key</Pill>;
    case 'scope':
      return (
        <Pill tone="accent">
          Scope <code>{route.requiredScope}</code>
        </Pill>
      );
    default:
      return <Pill tone="neutral">Any client key</Pill>;
  }
}

export function RoutesPage() {
  const notify = useToast();
  const routes = useLoad(() => api.routes.list(), []);
  const clusters = useLoad(() => api.clusters.list(), []);
  const [editing, setEditing] = useState<Route | 'new' | null>(null);
  const [deleting, setDeleting] = useState<Route | null>(null);

  const addButton = (
    <button type="button" className="button button--primary" onClick={() => setEditing('new')}>
      Add route
    </button>
  );

  return (
    <>
      <PageHeader
        title="Routes"
        lead="Which paths the gateway accepts, who may call them, and which backend receives the traffic."
        actions={routes.data?.length ? addButton : undefined}
      />

      <ErrorBanner message={routes.error ?? clusters.error} />
      {routes.loading && !routes.data && <Loading />}

      {routes.data && routes.data.length === 0 && (
        <EmptyState title="No routes yet">
          <p>Until you add a route, the gateway answers every request with 404.</p>
          {addButton}
        </EmptyState>
      )}

      {routes.data && routes.data.length > 0 && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Path</th>
                <th>Methods</th>
                <th>Who can call</th>
                <th>Backend</th>
                <th>State</th>
                <th>
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {routes.data.map((r) => (
                <tr key={r.id} className={r.isEnabled ? undefined : 'row--inactive'}>
                  <td>
                    <code className="path">{r.path}</code>
                    <span className="cell-note">
                      {r.id}
                      {r.description ? `: ${r.description}` : ''}
                    </span>
                  </td>
                  <td>{r.methods.length ? r.methods.join(', ') : 'All'}</td>
                  <td>
                    <AccessPill route={r} />
                  </td>
                  <td>
                    <a href="#/backends">
                      <code>{r.clusterId}</code>
                    </a>
                  </td>
                  <td>{r.isEnabled ? 'Live' : 'Off'}</td>
                  <td className="actions">
                    <button type="button" className="button button--small" onClick={() => setEditing(r)}>
                      Edit
                    </button>
                    <button
                      type="button"
                      className="button button--small button--danger-quiet"
                      onClick={() => setDeleting(r)}
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
        <RouteDrawer
          initial={editing === 'new' ? undefined : editing}
          clusters={clusters.data ?? []}
          onClose={() => setEditing(null)}
          onSaved={(route, created) => {
            setEditing(null);
            routes.reload();
            notify(created ? `Added route ${route.id}` : `Saved route ${route.id}`);
          }}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title={`Delete route ${deleting.id}?`}
          confirmLabel="Delete route"
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            await api.routes.remove(deleting.id);
            setDeleting(null);
            routes.reload();
            notify(`Deleted route ${deleting.id}`);
          }}
        >
          <p>
            Requests to <code>{deleting.path}</code> get 404 from the gateway once it reloads, within seconds.
          </p>
        </ConfirmDialog>
      )}
    </>
  );
}

function RouteDrawer(props: {
  initial?: Route;
  clusters: Cluster[];
  onSaved: (route: Route, created: boolean) => void;
  onClose: () => void;
}) {
  const { initial, clusters } = props;
  const formId = useId();
  const [id, setId] = useState(initial?.id ?? '');
  const [path, setPath] = useState(initial?.path ?? '');
  const [clusterId, setClusterId] = useState(initial?.clusterId ?? clusters[0]?.id ?? '');
  const [methods, setMethods] = useState<string[]>(initial?.methods ?? []);
  const [access, setAccess] = useState<Access>(initial ? accessOf(initial) : 'client');
  const [scope, setScope] = useState(initial?.requiredScope ?? '');
  const scopes = useLoad(() => api.scopes.list(), []);
  const [transforms, setTransforms] = useState(initial?.transformsJson ?? '');
  const [order, setOrder] = useState(initial?.order?.toString() ?? '');
  const [isEnabled, setIsEnabled] = useState(initial?.isEnabled ?? true);
  const [description, setDescription] = useState(initial?.description ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const toggleMethod = (method: string) =>
    setMethods((list) => (list.includes(method) ? list.filter((m) => m !== method) : [...list, method]));

  const save = async (e: FormEvent) => {
    e.preventDefault();
    const orderValue = order.trim() ? Number(order) : null;
    if (orderValue !== null && !Number.isInteger(orderValue)) {
      setError(new ApiError(400, 'Some fields need attention.', { order: ['Enter a whole number.'] }));
      return;
    }

    const input: RouteInput = {
      id: id.trim(),
      clusterId,
      path: path.trim(),
      methods: METHODS.filter((m) => methods.includes(m)),
      order: orderValue,
      requiredScope: access === 'scope' ? scope || null : null,
      allowAnonymous: access === 'public',
      transformsJson: transforms.trim() || null,
      isEnabled,
      description: description.trim() || null,
    };

    if (access === 'scope' && !input.requiredScope) {
      setError(new ApiError(400, 'Some fields need attention.', { requiredScope: ['Choose the scope keys must have.'] }));
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const saved = initial ? await api.routes.update(initial.id, input) : await api.routes.create(input);
      props.onSaved(saved, !initial);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  };

  const fieldErrors = error?.fieldErrors ?? {};

  return (
    <Drawer
      title={initial ? `Edit route ${initial.id}` : 'Add a route'}
      onClose={props.onClose}
      footer={
        <>
          <button type="button" className="button button--quiet" onClick={props.onClose}>
            Cancel
          </button>
          <button
            type="submit"
            form={formId}
            className="button button--primary"
            disabled={busy || clusters.length === 0}
          >
            {busy ? 'Saving…' : initial ? 'Save route' : 'Add route'}
          </button>
        </>
      }
    >
      {clusters.length === 0 ? (
        <EmptyState title="Add a backend first">
          <p>A route needs a backend to send traffic to.</p>
          <a className="button" href="#/backends" onClick={props.onClose}>
            Go to backends
          </a>
        </EmptyState>
      ) : (
        <form id={formId} onSubmit={save} noValidate className="form">
          {!initial && (
            <Field label="Route ID" hint="Lowercase letters, digits and dashes. Can't be changed later." errors={fieldErrors.id}>
              {(fid, d) => (
                <input
                  id={fid}
                  className="mono"
                  value={id}
                  placeholder="orders-read"
                  onChange={(e) => setId(e.target.value)}
                  aria-describedby={d}
                  spellCheck={false}
                />
              )}
            </Field>
          )}

          <Field
            label="Path"
            hint={
              <>
                <code>{'{**catch-all}'}</code> matches everything below a prefix.
              </>
            }
            errors={fieldErrors.path}
          >
            {(fid, d) => (
              <input
                id={fid}
                className="mono"
                value={path}
                placeholder="/api/orders/{**catch-all}"
                onChange={(e) => setPath(e.target.value)}
                aria-describedby={d}
                spellCheck={false}
              />
            )}
          </Field>

          <Field label="Backend" errors={fieldErrors.clusterId}>
            {(fid, d) => (
              <select id={fid} value={clusterId} onChange={(e) => setClusterId(e.target.value)} aria-describedby={d}>
                {clusters.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.id}
                  </option>
                ))}
              </select>
            )}
          </Field>

          <fieldset className="fieldset">
            <legend>Who can call this route</legend>
            <label className="radio">
              <input type="radio" name="access" checked={access === 'client'} onChange={() => setAccess('client')} />
              <span>
                Any client with a valid key
                <span className="checkbox__hint">The default for partner APIs.</span>
              </span>
            </label>
            <label className="radio">
              <input type="radio" name="access" checked={access === 'scope'} onChange={() => setAccess('scope')} />
              <span>
                Only keys with a scope
                <span className="checkbox__hint">For APIs only some partners may use.</span>
              </span>
            </label>
            {access === 'scope' && (
              <div className="fieldset__nested">
                <Field
                  label="Required scope"
                  hint={
                    scopes.data?.length === 0 ? (
                      <>
                        No scopes yet. <a href="#/scopes">Add one on the Scopes page</a> first.
                      </>
                    ) : (
                      <>
                        Keys need this scope to call the route. <a href="#/scopes">Manage scopes</a>
                      </>
                    )
                  }
                  errors={fieldErrors.requiredScope ?? (scopes.error ? [scopes.error] : undefined)}
                >
                  {(fid, d) => (
                    <select
                      id={fid}
                      className="mono input--short"
                      value={scope}
                      onChange={(e) => setScope(e.target.value)}
                      aria-describedby={d}
                      disabled={!scopes.data}
                    >
                      <option value="">{scopes.data ? 'Choose a scope' : 'Loading scopes…'}</option>
                      {scopes.data?.map((s) => (
                        <option key={s.name} value={s.name}>
                          {s.name}
                        </option>
                      ))}
                    </select>
                  )}
                </Field>
              </div>
            )}
            <label className="radio">
              <input type="radio" name="access" checked={access === 'public'} onChange={() => setAccess('public')} />
              <span>
                Anyone, no key needed
                <span className="checkbox__hint">Rate limited per IP. Use for public endpoints only.</span>
              </span>
            </label>
          </fieldset>

          <fieldset className="fieldset">
            <legend>HTTP methods</legend>
            <div className="method-grid">
              {METHODS.map((m) => (
                <label key={m} className="checkbox checkbox--compact">
                  <input type="checkbox" checked={methods.includes(m)} onChange={() => toggleMethod(m)} />
                  <span className="mono">{m}</span>
                </label>
              ))}
            </div>
            <p className="field__hint">Leave all unticked to accept every method.</p>
            {fieldErrors.methods && <p className="field__error">{fieldErrors.methods.join(' ')}</p>}
          </fieldset>

          <Field
            label="Transforms"
            hint={
              <>
                Optional YARP transforms as JSON, for example <code>{'[{"PathRemovePrefix": "/api"}]'}</code>.
              </>
            }
            errors={fieldErrors.transformsJson}
          >
            {(fid, d) => (
              <textarea
                id={fid}
                rows={4}
                className="mono"
                value={transforms}
                onChange={(e) => setTransforms(e.target.value)}
                aria-describedby={d}
                spellCheck={false}
              />
            )}
          </Field>

          <Field label="Match order" hint="Lower numbers are matched first. Leave empty for 0." errors={fieldErrors.order}>
            {(fid, d) => (
              <input
                id={fid}
                inputMode="numeric"
                className="input--short"
                value={order}
                onChange={(e) => setOrder(e.target.value)}
                aria-describedby={d}
              />
            )}
          </Field>

          <Field label="Notes" errors={fieldErrors.description}>
            {(fid, d) => (
              <textarea id={fid} rows={2} value={description} onChange={(e) => setDescription(e.target.value)} aria-describedby={d} />
            )}
          </Field>

          <Checkbox label="Route is live" hint="Turn off to stop matching it without deleting it." checked={isEnabled} onChange={setIsEnabled} />

          <ErrorBanner message={Object.keys(fieldErrors).length ? null : error?.message} />
        </form>
      )}
    </Drawer>
  );
}
