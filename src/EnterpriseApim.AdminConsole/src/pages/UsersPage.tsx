import { useId, useState, type FormEvent } from 'react';
import { api, type AdminUser } from '../api';
import { ConfirmDialog, Drawer, ErrorBanner, Field, Loading, PageHeader, Pill, useToast } from '../components';
import { formatDate, timeAgo } from '../format';
import { useLoad } from '../hooks';
import { PASSWORD_HINT } from './ChangePassword';
import { toApiError } from './ClientForm';

type Action = { kind: 'add' } | { kind: 'reset' | 'disable' | 'delete'; user: AdminUser };

export function UsersPage({ currentUserId }: { currentUserId: string }) {
  const notify = useToast();
  const users = useLoad(() => api.users.list(), []);
  const [action, setAction] = useState<Action | null>(null);

  const done = (message: string) => {
    setAction(null);
    notify(message);
    users.reload();
  };

  const enable = async (user: AdminUser) => {
    try {
      await api.users.enable(user.id);
      done(user.isActive ? `Unlocked ${user.username}` : `Enabled ${user.username}`);
    } catch (e) {
      notify(toApiError(e).message);
    }
  };

  return (
    <>
      <PageHeader
        title="Admin users"
        lead="People who can sign in to this console. Every admin can manage clients, routes, backends and other admins."
        actions={
          <button type="button" className="button button--primary" onClick={() => setAction({ kind: 'add' })}>
            Add admin
          </button>
        }
      />

      <ErrorBanner message={users.error} />
      {users.loading && !users.data && <Loading />}

      {users.data && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Username</th>
                <th>Status</th>
                <th>Last sign-in</th>
                <th>Added</th>
                <th>
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {users.data.map((u) => {
                const isMe = u.id === currentUserId;
                return (
                  <tr key={u.id} className={u.isActive ? undefined : 'row--inactive'}>
                    <td>
                      {u.username}
                      {isMe && <span className="cell-note">You</span>}
                    </td>
                    <td>
                      <StatusPill user={u} />
                    </td>
                    <td>{u.lastSignInAt ? timeAgo(u.lastSignInAt) : <span className="muted">Never</span>}</td>
                    <td>{formatDate(u.createdAt)}</td>
                    <td className="actions">
                      {!isMe && (
                        <>
                          {(!u.isActive || u.isLocked) && (
                            <button type="button" className="button" onClick={() => enable(u)}>
                              {u.isActive ? 'Unlock' : 'Enable'}
                            </button>
                          )}
                          <button type="button" className="button" onClick={() => setAction({ kind: 'reset', user: u })}>
                            Reset password
                          </button>
                          {u.isActive && (
                            <button
                              type="button"
                              className="button button--danger-quiet"
                              onClick={() => setAction({ kind: 'disable', user: u })}
                            >
                              Disable
                            </button>
                          )}
                          <button
                            type="button"
                            className="button button--danger-quiet"
                            onClick={() => setAction({ kind: 'delete', user: u })}
                          >
                            Delete
                          </button>
                        </>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {action?.kind === 'add' && (
        <PasswordDrawer
          title="Add an admin"
          submitLabel="Add admin"
          withUsername
          onClose={() => setAction(null)}
          onSubmit={async (username, password) => {
            const user = await api.users.create(username, password);
            done(`Added ${user.username}. Share the temporary password with them privately.`);
          }}
        />
      )}

      {action?.kind === 'reset' && (
        <PasswordDrawer
          title={`Reset password for ${action.user.username}`}
          submitLabel="Reset password"
          lead="They'll be signed out everywhere and must choose a new password at next sign-in."
          onClose={() => setAction(null)}
          onSubmit={async (_, password) => {
            await api.users.resetPassword(action.user.id, password);
            done(`Reset the password for ${action.user.username}`);
          }}
        />
      )}

      {action?.kind === 'disable' && (
        <ConfirmDialog
          title={`Disable ${action.user.username}?`}
          confirmLabel="Disable"
          onClose={() => setAction(null)}
          onConfirm={async () => {
            await api.users.disable(action.user.id);
            done(`Disabled ${action.user.username}`);
          }}
        >
          <p>They're signed out immediately and can't sign in until an admin enables them again.</p>
        </ConfirmDialog>
      )}

      {action?.kind === 'delete' && (
        <ConfirmDialog
          title={`Delete ${action.user.username}?`}
          confirmLabel="Delete admin"
          onClose={() => setAction(null)}
          onConfirm={async () => {
            await api.users.remove(action.user.id);
            done(`Deleted ${action.user.username}`);
          }}
        >
          <p>They're signed out immediately. This can't be undone; to block someone temporarily, disable them instead.</p>
        </ConfirmDialog>
      )}
    </>
  );
}

function StatusPill({ user }: { user: AdminUser }) {
  if (!user.isActive) return <Pill tone="danger">Disabled</Pill>;
  if (user.isLocked) return <Pill tone="warn">Locked</Pill>;
  if (user.mustChangePassword) return <Pill tone="neutral">Temporary password</Pill>;
  return <Pill tone="ok">Active</Pill>;
}

function PasswordDrawer(props: {
  title: string;
  submitLabel: string;
  lead?: string;
  withUsername?: boolean;
  onSubmit: (username: string, password: string) => Promise<void>;
  onClose: () => void;
}) {
  const formId = useId();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ReturnType<typeof toApiError> | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await props.onSubmit(username.trim(), password);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  };

  const fieldErrors = error?.fieldErrors ?? {};

  return (
    <Drawer
      title={props.title}
      onClose={props.onClose}
      footer={
        <>
          <button type="button" className="button button--quiet" onClick={props.onClose}>
            Cancel
          </button>
          <button type="submit" form={formId} className="button button--primary" disabled={busy}>
            {busy ? 'Saving…' : props.submitLabel}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} noValidate className="form">
        {props.lead && <p className="muted">{props.lead}</p>}
        {props.withUsername && (
          <Field
            label="Username"
            hint="Letters, digits, dots, dashes, underscores or @. Not case-sensitive."
            errors={fieldErrors.username}
          >
            {(id, d) => (
              <input
                id={id}
                autoComplete="off"
                autoCapitalize="none"
                spellCheck={false}
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                aria-describedby={d}
              />
            )}
          </Field>
        )}
        <Field
          label="Temporary password"
          hint={`${PASSWORD_HINT} They'll replace it at first sign-in.`}
          errors={fieldErrors.password}
        >
          {(id, d) => (
            <input
              id={id}
              type="password"
              autoComplete="new-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              aria-describedby={d}
            />
          )}
        </Field>
        <ErrorBanner message={Object.keys(fieldErrors).length ? null : error?.message} />
      </form>
    </Drawer>
  );
}
