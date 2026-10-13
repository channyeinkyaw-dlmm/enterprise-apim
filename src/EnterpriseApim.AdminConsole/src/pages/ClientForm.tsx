import { useId, useState, type FormEvent } from 'react';
import { api, ApiError, messageOf, type Tenant, type TenantInput } from '../api';
import { Checkbox, Drawer, ErrorBanner, Field } from '../components';
import { splitList } from '../format';

export function toApiError(error: unknown): ApiError {
  return error instanceof ApiError ? error : new ApiError(0, messageOf(error));
}

export function ClientFormDrawer(props: {
  initial?: Tenant;
  onSaved: (tenant: Tenant) => void;
  onClose: () => void;
}) {
  const { initial } = props;
  const formId = useId();
  const [name, setName] = useState(initial?.name ?? '');
  const [description, setDescription] = useState(initial?.description ?? '');
  const [isActive, setIsActive] = useState(initial?.isActive ?? true);
  const [rateLimit, setRateLimit] = useState(
    initial ? (initial.rateLimitPerMinute?.toString() ?? '') : '600',
  );
  const [ipRanges, setIpRanges] = useState((initial?.allowedIpRanges ?? []).join('\n'));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const save = async (e: FormEvent) => {
    e.preventDefault();
    const limitText = rateLimit.trim();
    const limit = limitText ? Number(limitText) : null;
    if (limit !== null && (!Number.isInteger(limit) || limit <= 0)) {
      setError(new ApiError(400, 'Some fields need attention.', {
        rateLimitPerMinute: ['Enter a whole number above 0, or leave empty for no limit.'],
      }));
      return;
    }

    const input: TenantInput = {
      name: name.trim(),
      description: description.trim() || null,
      isActive,
      rateLimitPerMinute: limit,
      allowedIpRanges: splitList(ipRanges),
    };

    setBusy(true);
    setError(null);
    try {
      const saved = initial ? await api.tenants.update(initial.id, input) : await api.tenants.create(input);
      props.onSaved(saved);
    } catch (err) {
      setError(toApiError(err));
      setBusy(false);
    }
  };

  const fieldErrors = error?.fieldErrors ?? {};

  return (
    <Drawer
      title={initial ? `Edit ${initial.name}` : 'Add a client'}
      onClose={props.onClose}
      footer={
        <>
          <button type="button" className="button button--quiet" onClick={props.onClose}>
            Cancel
          </button>
          <button type="submit" form={formId} className="button button--primary" disabled={busy}>
            {busy ? 'Saving…' : initial ? 'Save changes' : 'Add client'}
          </button>
        </>
      }
    >
      <form id={formId} onSubmit={save} noValidate className="form">
        <Field label="Organisation name" errors={fieldErrors.name}>
          {(id, d) => <input id={id} value={name} onChange={(e) => setName(e.target.value)} aria-describedby={d} />}
        </Field>

        <Field label="Notes" hint="Optional. Who the contact is, what the integration does." errors={fieldErrors.description}>
          {(id, d) => (
            <textarea id={id} rows={3} value={description} onChange={(e) => setDescription(e.target.value)} aria-describedby={d} />
          )}
        </Field>

        <Field
          label="Requests per minute"
          hint="Shared by all of this client's keys. Leave empty for no limit."
          errors={fieldErrors.rateLimitPerMinute}
        >
          {(id, d) => (
            <input
              id={id}
              inputMode="numeric"
              className="input--short"
              value={rateLimit}
              onChange={(e) => setRateLimit(e.target.value)}
              aria-describedby={d}
            />
          )}
        </Field>

        <Field
          label="Allowed IP addresses"
          hint="One address or CIDR range per line, for example 203.0.113.0/24. Leave empty to allow any IP."
          errors={fieldErrors.allowedIpRanges}
        >
          {(id, d) => (
            <textarea
              id={id}
              rows={4}
              className="mono"
              value={ipRanges}
              onChange={(e) => setIpRanges(e.target.value)}
              aria-describedby={d}
              spellCheck={false}
            />
          )}
        </Field>

        <Checkbox
          label="Client can call APIs"
          hint="Turn off to block every key of this client at once without revoking them."
          checked={isActive}
          onChange={setIsActive}
        />

        <ErrorBanner message={Object.keys(fieldErrors).length ? null : error?.message} />
      </form>
    </Drawer>
  );
}
