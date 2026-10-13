import { useId, useState, type FormEvent } from 'react';
import { api, type Scope } from '../api';
import { ConfirmDialog, Drawer, EmptyState, ErrorBanner, Field, Loading, PageHeader, useToast } from '../components';
import { formatDate } from '../format';
import { useLoad } from '../hooks';
import { toApiError } from './ClientForm';

export function ScopesPage() {
  const notify = useToast();
  const scopes = useLoad(() => api.scopes.list(), []);
  const [editing, setEditing] = useState<Scope | 'new' | null>(null);
  const [deleting, setDeleting] = useState<Scope | null>(null);

  const addButton = (
    <button type="button" className="button button--primary" onClick={() => setEditing('new')}>
      Add scope
    </button>
  );

  return (
    <>
      <PageHeader
        title="Scopes"
        lead="Permissions that routes can require and client keys can carry. Routes and keys choose from this list."
        actions={scopes.data?.length ? addButton : undefined}
      />

      <ErrorBanner message={scopes.error} />
      {scopes.loading && !scopes.data && <Loading />}

      {scopes.data && scopes.data.length === 0 && (
        <EmptyState title="No scopes yet">
          <p>
            Add a scope such as <code>orders.read</code>, then require it on a route and give it to the keys that may
            call that route.
          </p>
          {addButton}
        </EmptyState>
      )}

      {scopes.data && scopes.data.length > 0 && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Scope</th>
                <th className="num">Routes</th>
                <th className="num">Active keys</th>
                <th>Added</th>
                <th>
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {scopes.data.map((s) => (
                <tr key={s.name}>
                  <td>
                    <code>{s.name}</code>
                    {s.description && <span className="cell-note">{s.description}</span>}
                  </td>
                  <td className="num">{s.routeCount}</td>
                  <td className="num">{s.activeKeyCount}</td>
                  <td>{formatDate(s.createdAt)}</td>
                  <td className="actions">
                    <button type="button" className="button" onClick={() => setEditing(s)}>
                      Edit
                    </button>
                    <button type="button" className="button button--danger-quiet" onClick={() => setDeleting(s)}>
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
        <ScopeDrawer
          initial={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
          onSaved={(scope, created) => {
            setEditing(null);
            notify(created ? `Added ${scope.name}` : `Saved ${scope.name}`);
            scopes.reload();
          }}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title={`Delete ${deleting.name}?`}
          confirmLabel="Delete scope"
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            await api.scopes.remove(deleting.name);
            setDeleting(null);
            notify(`Deleted ${deleting.name}`);
            scopes.reload();
          }}
        >
          {deleting.routeCount > 0 || deleting.activeKeyCount > 0 ? (
            <p>
              This scope is still in use, so it can't be deleted yet. Change the routes that require it and revoke or
              replace the keys that carry it first.
            </p>
          ) : (
            <p>No route requires it and no active key carries it.</p>
          )}
        </ConfirmDialog>
      )}
    </>
  );
}

function ScopeDrawer(props: { initial?: Scope; onSaved: (scope: Scope, created: boolean) => void; onClose: () => void }) {
  const { initial } = props;
  const formId = useId();
  const [name, setName] = useState(initial?.name ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ReturnType<typeof toApiError> | null>(null);

  const save = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const text = description.trim() || null;
      const saved = initial ? await api.scopes.update(initial.name, text) : await api.scopes.create(name.trim(), text);
      props.onSaved(saved, !initial);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  };

  const fieldErrors = error?.fieldErrors ?? {};

  return (
    <Drawer
      title={initial ? `Edit ${initial.name}` : 'Add a scope'}
      onClose={props.onClose}
      footer={
        <>
          <button type="button" className="button button--quiet" onClick={props.onClose}>
            Cancel
          </button>
          <button type="submit" form={formId} className="button button--primary" disabled={busy}>
            {busy ? 'Saving…' : initial ? 'Save scope' : 'Add scope'}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={save} noValidate className="form">
        <Field
          label="Name"
          hint={
            initial
              ? "Keys carry this name, so it can't be changed. To rename, add a new scope and move routes and keys to it."
              : "Letters, digits, dots, dashes, colons or underscores, for example orders.read. It can't be changed later."
          }
          errors={fieldErrors.name}
        >
          {(id, d) => (
            <input
              id={id}
              className="mono"
              value={name}
              placeholder="orders.read"
              onChange={(e) => setName(e.target.value)}
              aria-describedby={d}
              spellCheck={false}
              readOnly={!!initial}
            />
          )}
        </Field>

        <Field label="Description" hint="Optional. What a key with this scope may do." errors={fieldErrors.description}>
          {(id, d) => (
            <textarea id={id} rows={3} value={description} onChange={(e) => setDescription(e.target.value)} aria-describedby={d} />
          )}
        </Field>

        <ErrorBanner message={Object.keys(fieldErrors).length ? null : error?.message} />
      </form>
    </Drawer>
  );
}
