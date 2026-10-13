import { useId, useState, type FormEvent } from 'react';
import { ALL_SCOPES, api, ApiError, type ApiKey, type CreatedApiKey, type Tenant } from '../api';
import { Checkbox, Drawer, ErrorBanner, Field, Loading } from '../components';
import { formatDate } from '../format';
import { useLoad } from '../hooks';
import { toApiError } from './ClientForm';

/** yyyy-mm-dd in local time, for a date input. */
function toDateInput(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

type KeyDrawerProps =
  | { tenant: Tenant; initial?: undefined; onIssued: (created: CreatedApiKey) => void; onClose: () => void }
  | { tenant: Tenant; initial: ApiKey; onSaved: (key: ApiKey) => void; onClose: () => void };

/** Issues a new key, or edits an existing key's name, scopes and expiry. The secret never changes. */
export function KeyDrawer(props: KeyDrawerProps) {
  const { initial } = props;
  const formId = useId();
  const [name, setName] = useState(initial?.name ?? '');
  const [chosen, setChosen] = useState<string[]>(initial?.scopes ?? []);
  const initialExpiresOn = toDateInput(initial?.expiresAt ?? null);
  const [expiresOn, setExpiresOn] = useState(initialExpiresOn);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const catalog = useLoad(() => api.scopes.list(), []);
  const allScopes = chosen.includes(ALL_SCOPES);

  const toggleScope = (scope: string, on: boolean) =>
    setChosen((list) => (on ? [...list, scope] : list.filter((s) => s !== scope)));

  const save = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const input = {
        name: name.trim(),
        scopes: allScopes ? [ALL_SCOPES] : chosen,
        // Valid until the end of the chosen day, local time. An unchanged date keeps the exact original time.
        expiresAt:
          initial && expiresOn === initialExpiresOn
            ? initial.expiresAt
            : expiresOn
              ? new Date(`${expiresOn}T23:59:59`).toISOString()
              : null,
      };
      if (props.initial) {
        props.onSaved(await api.keys.update(props.tenant.id, props.initial.keyId, input));
      } else {
        props.onIssued(await api.keys.create(props.tenant.id, input));
      }
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  };

  const fieldErrors = error?.fieldErrors ?? {};

  return (
    <Drawer
      title={initial ? `Edit key ${initial.name}` : `Issue a key to ${props.tenant.name}`}
      onClose={props.onClose}
      footer={
        <>
          <button type="button" className="button button--quiet" onClick={props.onClose}>
            Cancel
          </button>
          <button type="submit" form={formId} className="button button--primary" disabled={busy}>
            {busy ? (initial ? 'Saving…' : 'Issuing…') : initial ? 'Save key' : 'Issue key'}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={save} noValidate className="form">
        {initial && (
          <p className="muted">
            Key <code>{initial.keyId}</code>. The client keeps using the same key; changes apply on every gateway
            within seconds.
          </p>
        )}
        <Field
          label="Key name"
          hint="Where the key will be used, so you know what breaks if you revoke it."
          errors={fieldErrors.name}
        >
          {(id, d) => (
            <input
              id={id}
              value={name}
              placeholder="Production ERP integration"
              onChange={(e) => setName(e.target.value)}
              aria-describedby={d}
            />
          )}
        </Field>

        <fieldset className="fieldset">
          <legend>Scopes</legend>
          <p className="field__hint">Needed only for routes that require a scope. Manage the list on the Scopes page.</p>
          {catalog.loading && !catalog.data && <Loading label="Loading scopes" />}
          <ErrorBanner message={catalog.error} />
          {catalog.data?.length === 0 && (
            <p className="muted">
              No scopes yet. <a href="#/scopes">Add scopes</a> if some routes should only be open to some keys.
            </p>
          )}
          {catalog.data?.map((scope) => (
            <Checkbox
              key={scope.name}
              label={scope.name}
              hint={scope.description ?? undefined}
              checked={allScopes || chosen.includes(scope.name)}
              disabled={allScopes}
              onChange={(on) => toggleScope(scope.name, on)}
            />
          ))}
          {catalog.data && catalog.data.length > 0 && (
            <Checkbox
              label="All scopes"
              hint="Includes scopes added later. Use only for fully trusted integrations."
              checked={allScopes}
              onChange={(on) => setChosen(on ? [ALL_SCOPES] : [])}
            />
          )}
          {fieldErrors.scopes && <p className="field__error">{fieldErrors.scopes.join(' ')}</p>}
        </fieldset>

        <Field
          label="Expires on"
          hint={
            initial?.status === 'expired'
              ? `This key expired on ${formatDate(initial.expiresAt)}. Choose a later date, or clear it, to make it work again.`
              : 'Optional. Leave empty for a key that works until you revoke it.'
          }
          errors={fieldErrors.expiresAt}
        >
          {(id, d) => (
            <input
              id={id}
              type="date"
              className="input--short"
              value={expiresOn}
              onChange={(e) => setExpiresOn(e.target.value)}
              aria-describedby={d}
            />
          )}
        </Field>

        <ErrorBanner message={Object.keys(fieldErrors).length ? null : error?.message} />
      </form>
    </Drawer>
  );
}
