import { useCallback, useEffect, useState } from 'react';
import { messageOf } from './api';

type LoadState<T> = { data?: T; error?: string; loading: boolean };

/** Loads data on mount and whenever deps change; `reload` re-runs it. */
export function useLoad<T>(load: () => Promise<T>, deps: unknown[]) {
  const [state, setState] = useState<LoadState<T>>({ loading: true });
  const [version, setVersion] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setState((previous) => ({ ...previous, loading: true }));
    load().then(
      (data) => !cancelled && setState({ data, loading: false }),
      (error: unknown) => !cancelled && setState({ error: messageOf(error), loading: false }),
    );
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, version]);

  const reload = useCallback(() => setVersion((v) => v + 1), []);
  return { ...state, reload };
}

export type View =
  | { page: 'clients' }
  | { page: 'client'; id: string }
  | { page: 'routes' }
  | { page: 'backends' }
  | { page: 'scopes' }
  | { page: 'users' };

function parseHash(hash: string): View {
  const [section, id] = hash.replace(/^#\/?/, '').split('/');
  if (section === 'routes') return { page: 'routes' };
  if (section === 'backends') return { page: 'backends' };
  if (section === 'scopes') return { page: 'scopes' };
  if (section === 'users') return { page: 'users' };
  if (section === 'clients' && id) return { page: 'client', id: decodeURIComponent(id) };
  return { page: 'clients' };
}

export function navigate(path: string) {
  window.location.hash = path;
}

/** Tiny hash router so views survive reloads and the back button works. */
export function useHashView(): View {
  const [view, setView] = useState<View>(() => parseHash(window.location.hash));
  useEffect(() => {
    const onChange = () => setView(parseHash(window.location.hash));
    window.addEventListener('hashchange', onChange);
    return () => window.removeEventListener('hashchange', onChange);
  }, []);
  return view;
}
