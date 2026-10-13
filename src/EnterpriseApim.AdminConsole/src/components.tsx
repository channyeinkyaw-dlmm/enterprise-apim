import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useId,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import type { KeyStatus } from './api';

/* ---------- Page header ---------- */

export function PageHeader(props: { title: ReactNode; lead?: ReactNode; actions?: ReactNode; back?: ReactNode }) {
  return (
    <header className="page-header">
      {props.back && <div className="page-header__back">{props.back}</div>}
      <div className="page-header__row">
        <div>
          <h1>{props.title}</h1>
          {props.lead && <p className="page-header__lead">{props.lead}</p>}
        </div>
        {props.actions && <div className="page-header__actions">{props.actions}</div>}
      </div>
    </header>
  );
}

/* ---------- Status pills ---------- */

type Tone = 'ok' | 'warn' | 'danger' | 'neutral' | 'accent';

export function Pill({ tone, children }: { tone: Tone; children: ReactNode }) {
  return <span className={`pill pill--${tone}`}>{children}</span>;
}

export function KeyStatusPill({ status }: { status: KeyStatus }) {
  if (status === 'active') return <Pill tone="ok">Active</Pill>;
  if (status === 'expired') return <Pill tone="warn">Expired</Pill>;
  return <Pill tone="danger">Revoked</Pill>;
}

/* ---------- Feedback ---------- */

export function ErrorBanner({ message }: { message?: string | null }) {
  if (!message) return null;
  return (
    <div className="banner banner--error" role="alert">
      {message}
    </div>
  );
}

export function Loading({ label = 'Loading' }: { label?: string }) {
  return (
    <div className="loading" role="status">
      {label}…
    </div>
  );
}

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="empty">
      <p className="empty__title">{title}</p>
      {children}
    </div>
  );
}

/* ---------- Form fields ---------- */

export function Field(props: {
  label: string;
  hint?: ReactNode;
  errors?: string[];
  children: (id: string, describedBy: string | undefined) => ReactNode;
}) {
  const id = useId();
  const hintId = props.hint ? `${id}-hint` : undefined;
  const errorId = props.errors?.length ? `${id}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined;

  return (
    <div className={`field${props.errors?.length ? ' field--invalid' : ''}`}>
      <label htmlFor={id}>{props.label}</label>
      {props.children(id, describedBy)}
      {props.hint && (
        <p className="field__hint" id={hintId}>
          {props.hint}
        </p>
      )}
      {props.errors?.length ? (
        <p className="field__error" id={errorId}>
          {props.errors.join(' ')}
        </p>
      ) : null}
    </div>
  );
}

export function Checkbox(props: {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  hint?: string;
  disabled?: boolean;
}) {
  return (
    <label className="checkbox">
      <input
        type="checkbox"
        checked={props.checked}
        disabled={props.disabled}
        onChange={(e) => props.onChange(e.target.checked)}
      />
      <span>
        {props.label}
        {props.hint && <span className="checkbox__hint">{props.hint}</span>}
      </span>
    </label>
  );
}

/* ---------- Overlays ---------- */

function useEscape(onClose: () => void) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
}

function useInitialFocus<T extends HTMLElement>() {
  const ref = useRef<T>(null);
  useEffect(() => {
    const target = ref.current?.querySelector<HTMLElement>('input, select, textarea, button');
    target?.focus();
  }, []);
  return ref;
}

/** Side panel for create/edit forms; the list behind it stays visible. */
export function Drawer(props: { title: string; onClose: () => void; children: ReactNode; footer: ReactNode }) {
  useEscape(props.onClose);
  const ref = useInitialFocus<HTMLDivElement>();
  const titleId = useId();

  return (
    <div className="overlay" onMouseDown={(e) => e.target === e.currentTarget && props.onClose()}>
      <div className="drawer" role="dialog" aria-modal="true" aria-labelledby={titleId} ref={ref}>
        <div className="drawer__head">
          <h2 id={titleId}>{props.title}</h2>
          <button type="button" className="icon-button" onClick={props.onClose} aria-label="Close">
            ×
          </button>
        </div>
        <div className="drawer__body">{props.children}</div>
        <div className="drawer__foot">{props.footer}</div>
      </div>
    </div>
  );
}

export function Modal(props: {
  title: string;
  onClose?: () => void;
  children: ReactNode;
  footer: ReactNode;
  wide?: boolean;
}) {
  const close = props.onClose ?? (() => {});
  useEscape(close);
  const ref = useInitialFocus<HTMLDivElement>();
  const titleId = useId();

  return (
    <div className="overlay overlay--center" onMouseDown={(e) => e.target === e.currentTarget && close()}>
      <div
        className={`modal${props.wide ? ' modal--wide' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        ref={ref}
      >
        <h2 id={titleId}>{props.title}</h2>
        <div className="modal__body">{props.children}</div>
        <div className="modal__foot">{props.footer}</div>
      </div>
    </div>
  );
}

/** Confirmation dialog for destructive actions. */
export function ConfirmDialog(props: {
  title: string;
  confirmLabel: string;
  onConfirm: () => Promise<void>;
  onClose: () => void;
  children: ReactNode;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const confirm = async () => {
    setBusy(true);
    setError(null);
    try {
      await props.onConfirm();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'The action failed.');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={props.title}
      onClose={props.onClose}
      footer={
        <>
          <button type="button" className="button button--quiet" onClick={props.onClose}>
            Cancel
          </button>
          <button type="button" className="button button--danger" onClick={confirm} disabled={busy}>
            {busy ? 'Working…' : props.confirmLabel}
          </button>
        </>
      }
    >
      {props.children}
      <ErrorBanner message={error} />
    </Modal>
  );
}

/* ---------- Toasts ---------- */

const ToastContext = createContext<(message: string) => void>(() => {});

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<{ id: number; message: string }[]>([]);
  const nextId = useRef(1);

  const notify = useCallback((message: string) => {
    const id = nextId.current++;
    setToasts((list) => [...list, { id, message }]);
    window.setTimeout(() => setToasts((list) => list.filter((t) => t.id !== id)), 3500);
  }, []);

  return (
    <ToastContext.Provider value={notify}>
      {children}
      <div className="toasts" role="status" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className="toast">
            {t.message}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  return useContext(ToastContext);
}
