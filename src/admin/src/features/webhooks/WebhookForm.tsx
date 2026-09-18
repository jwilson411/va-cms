/**
 * WebhookForm — register a new webhook subscriber.
 * Issue #54 / #168: the URL must be https and on the webhooks.allowedHosts site setting;
 * the API enforces both and the form surfaces its message verbatim.
 */

import React, { useState } from 'react';
import { WEBHOOK_EVENTS, type WebhookRegistrationRequest } from './types';

interface Props {
  onSubmit: (values: WebhookRegistrationRequest) => void;
  onCancel: () => void;
  isSubmitting: boolean;
  submitError: string | null;
}

export function WebhookForm({ onSubmit, onCancel, isSubmitting, submitError }: Props) {
  const [name, setName]     = useState('');
  const [url, setUrl]       = useState('');
  const [secret, setSecret] = useState('');
  const [events, setEvents] = useState<string[]>(['content.published']);
  const [error, setError]   = useState<string | null>(null);

  function toggleEvent(evt: string) {
    setEvents((cur) => (cur.includes(evt) ? cur.filter((e) => e !== evt) : [...cur, evt]));
  }

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!url.trim()) { setError('URL is required.'); return; }
    if (!/^https?:\/\//i.test(url.trim())) { setError('URL must start with https:// (http:// is only accepted in Development).'); return; }
    if (events.length === 0) { setError('Select at least one event.'); return; }
    onSubmit({
      name:   name.trim() || undefined,
      url:    url.trim(),
      secret: secret.trim() || undefined,
      events,
    });
  }

  const shownError = error ?? submitError;

  return (
    <form className="usa-form usa-form--large" onSubmit={handleSubmit} noValidate>
      {shownError && (
        <div className="usa-alert usa-alert--error usa-alert--slim margin-bottom-2" role="alert">
          <div className="usa-alert__body"><p className="usa-alert__text">{shownError}</p></div>
        </div>
      )}

      <label className="usa-label" htmlFor="webhook-name">Name</label>
      <input id="webhook-name" className="usa-input" type="text" maxLength={200}
        value={name} onChange={(e) => setName(e.target.value)} />

      <label className="usa-label" htmlFor="webhook-url">Endpoint URL <span className="text-secondary-dark">*</span></label>
      <span className="usa-hint" id="webhook-url-hint">
        https:// and a host on the <code>webhooks.allowedHosts</code> setting. Private, loopback and link-local addresses are refused.
      </span>
      <input id="webhook-url" className="usa-input" type="url" required maxLength={2000}
        aria-describedby="webhook-url-hint" value={url} onChange={(e) => setUrl(e.target.value)} />

      <label className="usa-label" htmlFor="webhook-secret">Signing secret</label>
      <span className="usa-hint" id="webhook-secret-hint">Optional — leave blank to have one generated. Shown once after registration.</span>
      <input id="webhook-secret" className="usa-input" type="password" maxLength={200} autoComplete="off"
        aria-describedby="webhook-secret-hint" value={secret} onChange={(e) => setSecret(e.target.value)} />

      <fieldset className="usa-fieldset margin-top-2">
        <legend className="usa-legend">Events</legend>
        {WEBHOOK_EVENTS.map((evt) => (
          <div className="usa-checkbox" key={evt}>
            <input className="usa-checkbox__input" id={`webhook-event-${evt}`} type="checkbox"
              checked={events.includes(evt)} onChange={() => toggleEvent(evt)} />
            <label className="usa-checkbox__label" htmlFor={`webhook-event-${evt}`}>{evt}</label>
          </div>
        ))}
      </fieldset>

      <div className="margin-top-3 display-flex flex-gap-1">
        <button type="submit" className="usa-button" disabled={isSubmitting}>
          {isSubmitting ? 'Registering…' : 'Register webhook'}
        </button>
        <button type="button" className="usa-button usa-button--outline" onClick={onCancel} disabled={isSubmitting}>
          Cancel
        </button>
      </div>
    </form>
  );
}
