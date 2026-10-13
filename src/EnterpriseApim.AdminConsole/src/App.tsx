import { useEffect, useState } from 'react';
import { api, messageOf, setUnauthorizedHandler, type CurrentUser } from './api';
import { Loading, useToast } from './components';
import { useHashView, type View } from './hooks';
import { BackendsPage } from './pages/BackendsPage';
import { ChangePasswordModal, RequiredPasswordChangePage } from './pages/ChangePassword';
import { ClientDetailPage } from './pages/ClientDetailPage';
import { ClientsPage } from './pages/ClientsPage';
import { LoginPage } from './pages/LoginPage';
import { RoutesPage } from './pages/RoutesPage';
import { ScopesPage } from './pages/ScopesPage';
import { UsersPage } from './pages/UsersPage';

// undefined: still checking for an existing session cookie; null: signed out.
type SessionState = CurrentUser | null | undefined;

export function App() {
  const [user, setUser] = useState<SessionState>(undefined);

  useEffect(() => {
    setUnauthorizedHandler(() => setUser(null));
    api.auth.me().then(setUser, () => setUser(null));
  }, []);

  const signOut = async () => {
    try {
      await api.auth.signOut();
    } finally {
      setUser(null);
    }
  };

  if (user === undefined) return <Loading label="Checking your session" />;
  if (user === null) return <LoginPage onSignedIn={setUser} />;
  if (user.mustChangePassword) {
    return <RequiredPasswordChangePage user={user} onChanged={setUser} onSignOut={signOut} />;
  }
  return <Shell user={user} onUserChanged={setUser} onSignOut={signOut} />;
}

const NAV: { href: string; label: string; active: (v: View) => boolean }[] = [
  { href: '#/clients', label: 'Clients', active: (v) => v.page === 'clients' || v.page === 'client' },
  { href: '#/routes', label: 'Routes', active: (v) => v.page === 'routes' },
  { href: '#/backends', label: 'Backends', active: (v) => v.page === 'backends' },
  { href: '#/scopes', label: 'Scopes', active: (v) => v.page === 'scopes' },
  { href: '#/users', label: 'Admin users', active: (v) => v.page === 'users' },
];

function Shell(props: { user: CurrentUser; onUserChanged: (user: CurrentUser) => void; onSignOut: () => void }) {
  const view = useHashView();
  const notify = useToast();
  const [reloading, setReloading] = useState(false);
  const [changingPassword, setChangingPassword] = useState(false);
  // On small screens the navigation folds into a menu; close it whenever the page changes.
  const [menuOpen, setMenuOpen] = useState(false);
  useEffect(() => setMenuOpen(false), [view]);

  const reload = async () => {
    setReloading(true);
    try {
      await api.reloadGateways();
      notify('Gateways are reloading routes and keys');
    } catch (e) {
      notify(messageOf(e));
    } finally {
      setReloading(false);
    }
  };

  return (
    <div className="shell">
      <nav className={`rail${menuOpen ? ' rail--open' : ''}`} aria-label="Main">
        <div className="rail__top">
          <a className="brand" href="#/clients">
            Enterprise APIM
          </a>
          <button
            type="button"
            className="rail__menu"
            aria-expanded={menuOpen}
            aria-controls="rail-body"
            onClick={() => setMenuOpen((open) => !open)}
          >
            {menuOpen ? 'Close' : 'Menu'}
          </button>
        </div>
        <div className="rail__body" id="rail-body">
          <ul className="rail__nav">
            {NAV.map((item) => (
              <li key={item.href}>
                <a href={item.href} aria-current={item.active(view) ? 'page' : undefined}>
                  {item.label}
                </a>
              </li>
            ))}
          </ul>
          <div className="rail__foot">
            <button
              type="button"
              className="rail__action"
              onClick={reload}
              disabled={reloading}
              title="Changes apply automatically. Use this if a gateway seems out of date."
            >
              {reloading ? 'Reloading…' : 'Reload gateways'}
            </button>
            <span className="rail__user" title="Signed in as">
              {props.user.username}
            </span>
            <button type="button" className="rail__action" onClick={() => setChangingPassword(true)}>
              Change password
            </button>
            <button type="button" className="rail__action" onClick={props.onSignOut}>
              Sign out
            </button>
          </div>
        </div>
      </nav>

      <main className="main" id="main">
        {view.page === 'clients' && <ClientsPage />}
        {view.page === 'client' && <ClientDetailPage key={view.id} id={view.id} />}
        {view.page === 'routes' && <RoutesPage />}
        {view.page === 'backends' && <BackendsPage />}
        {view.page === 'scopes' && <ScopesPage />}
        {view.page === 'users' && <UsersPage currentUserId={props.user.id} />}
      </main>

      {changingPassword && (
        <ChangePasswordModal
          onClose={() => setChangingPassword(false)}
          onChanged={(user) => {
            setChangingPassword(false);
            props.onUserChanged(user);
            notify('Password changed. Your other sessions have ended.');
          }}
        />
      )}
    </div>
  );
}
