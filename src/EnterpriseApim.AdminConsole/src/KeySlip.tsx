import { useEffect, useRef, useState } from 'react';

/**
 * Shows a newly issued key split into its three parts, so it's clear which part
 * is safe to share in logs (the key ID) and which part is the secret.
 */
export function KeySlip({ plainKey }: { plainKey: string }) {
  const [copied, setCopied] = useState(false);
  const [manualCopy, setManualCopy] = useState(false);
  const rawRef = useRef<HTMLInputElement>(null);
  const [prefix, keyId, ...rest] = plainKey.split('_');
  const secret = rest.join('_');

  useEffect(() => {
    if (manualCopy) rawRef.current?.select();
  }, [manualCopy]);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(plainKey);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2500);
    } catch {
      // The clipboard API can be blocked (for example on plain http); let the user copy by hand.
      setManualCopy(true);
    }
  };

  return (
    <div className="key-slip">
      <p className="key-slip__value" aria-label={`New access key ${plainKey}`}>
        <span className="key-slip__seg key-slip__seg--prefix">
          <code>{prefix}_</code>
          <span className="key-slip__label">Prefix</span>
        </span>
        <span className="key-slip__seg key-slip__seg--id">
          <code>{keyId}_</code>
          <span className="key-slip__label">Key ID, safe to log</span>
        </span>
        <span className="key-slip__seg key-slip__seg--secret">
          <code>{secret}</code>
          <span className="key-slip__label">Secret, shown only now</span>
        </span>
      </p>

      {manualCopy && (
        <input
          ref={rawRef}
          className="key-slip__raw"
          readOnly
          value={plainKey}
          aria-label="Access key, select and copy"
          onFocus={(e) => e.target.select()}
        />
      )}

      <div className="key-slip__actions">
        <button type="button" className="button button--primary" onClick={copy}>
          {copied ? 'Copied' : 'Copy key'}
        </button>
        <span className="key-slip__usage">
          {manualCopy ? (
            'Copying is blocked in this browser. Select the key above and copy it.'
          ) : (
            <>
              Clients send it in the <code>X-Api-Key</code> header.
            </>
          )}
        </span>
      </div>
    </div>
  );
}
