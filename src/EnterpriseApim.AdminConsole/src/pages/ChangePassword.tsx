import { useId, useState, type FormEvent } from 'react';
import { api, type CurrentUser } from '../api';
import { ErrorBanner, Field, Modal } from '../components';
import { AuthLayout } from './LoginPage';
import { toApiError } from './ClientForm';

export const PASSWORD_HINT = 'At least 12 characters, without your username. A few unrelated words work well.';

/** Shown after sign-in when the account has a temporary password. Nothing else is reachable until it's changed. */
export function RequiredPasswordChangePage(props: {
  user: CurrentUser;
  onChanged: (user: CurrentUser) => void;
  onSignOut: () => void;
}) {
  const formId = useId();
  return (
    <AuthLayout>
      <div className="login__panel">
        <h1>Choose a new password</h1>
        <p className="muted">
          You signed in as <strong>{props.user.username}</strong> with a temporary password. Replace it to continue.
        </p>
        <PasswordForm id={formId} onChanged={props.onChanged} />
        <button type="submit" form={formId} className="button button--primary button--block">
          Save password and continue
        </button>
        <button type="button" className="button button--quiet button--block" onClick={props.onSignOut}>
          Sign out
        </button>
      </div>
    </AuthLayout>
  );
}

export function ChangePasswordModal(props: { onChanged: (user: CurrentUser) => void; onClose: () => void }) {
  const formId = useId();
  return (
    <Modal
      title="Change your password"
      onClose={props.onClose}
      footer={
        <>
          <button type="button" className="button button--quiet" onClick={props.onClose}>
            Cancel
          </button>
          <button type="submit" form={formId} className="button button--primary">
            Change password
          </button>
        </>
      }
    >
      <p className="muted">Your other signed-in sessions will end. This one stays signed in.</p>
      <PasswordForm id={formId} onChanged={props.onChanged} />
    </Modal>
  );
}

function PasswordForm(props: { id: string; onChanged: (user: CurrentUser) => void }) {
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ReturnType<typeof toApiError> | null>(null);
  const [mismatch, setMismatch] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (busy) return;
    setMismatch(next !== confirm);
    if (next !== confirm) return;

    setBusy(true);
    setError(null);
    try {
      props.onChanged(await api.auth.changePassword(current, next));
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  };

  const fieldErrors = error?.fieldErrors ?? {};

  return (
    <form id={props.id} onSubmit={submit} noValidate className="form">
      <Field label="Current password" errors={fieldErrors.currentPassword}>
        {(id, d) => (
          <input
            id={id}
            type="password"
            autoComplete="current-password"
            value={current}
            onChange={(e) => setCurrent(e.target.value)}
            aria-describedby={d}
            autoFocus
          />
        )}
      </Field>
      <Field label="New password" hint={PASSWORD_HINT} errors={fieldErrors.newPassword}>
        {(id, d) => (
          <input
            id={id}
            type="password"
            autoComplete="new-password"
            value={next}
            onChange={(e) => setNext(e.target.value)}
            aria-describedby={d}
          />
        )}
      </Field>
      <Field label="Repeat new password" errors={mismatch ? ["The passwords don't match."] : undefined}>
        {(id, d) => (
          <input
            id={id}
            type="password"
            autoComplete="new-password"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            aria-describedby={d}
          />
        )}
      </Field>
      <ErrorBanner message={Object.keys(fieldErrors).length ? null : error?.message} />
    </form>
  );
}
