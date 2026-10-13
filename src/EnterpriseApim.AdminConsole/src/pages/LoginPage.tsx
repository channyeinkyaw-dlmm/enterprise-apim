import { useState, type FormEvent, type ReactNode } from 'react';
import { api, messageOf, type CurrentUser } from '../api';
import { ErrorBanner, Field } from '../components';

export function LoginPage({ onSignedIn }: { onSignedIn: (user: CurrentUser) => void }) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!username.trim() || !password) {
      setError('Enter your username and password.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      onSignedIn(await api.auth.signIn(username.trim(), password));
    } catch (err) {
      setError(messageOf(err));
      setPassword('');
      setBusy(false);
    }
  };

  return (
    <AuthLayout>
      <form onSubmit={submit} noValidate>
        <h1>Sign in to the admin console</h1>
        <Field label="Username">
          {(id, describedBy) => (
            <input
              id={id}
              autoComplete="username"
              autoCapitalize="none"
              spellCheck={false}
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              aria-describedby={describedBy}
              autoFocus
            />
          )}
        </Field>
        <Field label="Password">
          {(id, describedBy) => (
            <input
              id={id}
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              aria-describedby={describedBy}
            />
          )}
        </Field>
        <ErrorBanner message={error} />
        <button type="submit" className="button button--primary button--block" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </AuthLayout>
  );
}

export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="login">
      <section className="login__intro">
        <p className="brand brand--large">Enterprise APIM</p>
        <p className="login__statement">Decide which partner systems get through the gateway, and where their requests go.</p>
      </section>
      <section className="login__form">{children}</section>
    </div>
  );
}
