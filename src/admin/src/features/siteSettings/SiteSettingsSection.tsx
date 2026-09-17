/**
 * SiteSettingsSection — "Site Settings" section of Admin → Settings (issue #143, epic #141).
 *
 * Renders every runtime setting the API declares, grouped by category, with an input that
 * matches its data type:
 *   bool   → USWDS checkbox
 *   int    → number input
 *   string → text input
 *   json   → textarea
 * Each row saves on its own and can be reset to the code default. Changes take effect in
 * the running API immediately — no build, no restart.
 *
 * Accessible:
 *   - Every input has a <label>; descriptions and errors are linked via aria-describedby.
 *   - Save/reset outcomes are announced through role="status" / role="alert".
 *   - USWDS 3.x classes only.
 */

import React, { useEffect, useMemo, useState } from 'react';
import {
  useResetSiteSetting,
  useSetSiteSetting,
  useSiteSettingsAdmin,
  type SiteSettingDto,
} from './useSiteSettingsAdmin';

const CATEGORY_ORDER = [
  'Site',
  'Analytics',
  'Features',
  'Media',
  'Search',
  'Workflow',
  'Notifications',
  'Admin',
  'Auth',
  'Webhooks',
  'Api',
];

const CATEGORY_HELP: Record<string, string> = {
  Site: 'Names and links shown in the public site header, footer and identifier.',
  Analytics: 'Digital Analytics Program (DAP) script on the public site.',
  Features: 'Switch whole capabilities on or off without a deploy.',
  Media: 'Upload limits and image processing.',
  Search: 'Page sizes for public and API search.',
  Workflow: 'Scheduler cadence and review rules.',
  Notifications: 'Admin bell polling.',
  Admin: 'Editor autosave and list page sizes in this admin app.',
  Auth: 'Token lifetimes. Signing keys and the auth mode stay in server configuration.',
  Webhooks: 'Delivery retry policy for registered webhooks.',
  Api: 'Limits applied across API list endpoints.',
};

const SCOPE_LABEL: Record<SiteSettingDto['scope'], string> = {
  Server: 'API only',
  Admin: 'API + admin app',
  Public: 'Public site',
};

function idFor(key: string): string {
  return `site-setting-${key.replace(/[^a-z0-9]/gi, '-')}`;
}

function groupByCategory(items: SiteSettingDto[]): Array<[string, SiteSettingDto[]]> {
  const map = new Map<string, SiteSettingDto[]>();
  for (const item of items) {
    const list = map.get(item.category) ?? [];
    list.push(item);
    map.set(item.category, list);
  }
  const rank = (c: string) => {
    const i = CATEGORY_ORDER.indexOf(c);
    return i === -1 ? CATEGORY_ORDER.length : i;
  };
  return [...map.entries()]
    .sort(([a], [b]) => rank(a) - rank(b) || a.localeCompare(b))
    .map(([cat, list]) => [cat, [...list].sort((x, y) => x.sortOrder - y.sortOrder || x.key.localeCompare(y.key))]);
}

interface RowProps {
  setting: SiteSettingDto;
}

function SettingRow({ setting }: RowProps): JSX.Element {
  const setMutation = useSetSiteSetting();
  const resetMutation = useResetSiteSetting();

  const serverValue = setting.effectiveValue ?? '';
  const [draft, setDraft] = useState<string>(serverValue);
  const [status, setStatus] = useState<{ kind: 'ok' | 'error'; text: string } | null>(null);

  // Follow the server whenever it changes (after save/reset or a refetch).
  useEffect(() => {
    setDraft(serverValue);
  }, [serverValue]);

  const isDirty = draft !== serverValue;
  const busy = setMutation.isPending || resetMutation.isPending;
  const inputId = idFor(setting.key);
  const hintId = `${inputId}-hint`;
  const statusId = `${inputId}-status`;
  const describedBy = [setting.description ? hintId : null, status ? statusId : null]
    .filter(Boolean)
    .join(' ') || undefined;

  const save = (value: string) => {
    setStatus(null);
    setMutation.mutate(
      { key: setting.key, value },
      {
        onSuccess: () => setStatus({ kind: 'ok', text: 'Saved. The change is live.' }),
        onError: (err) => setStatus({ kind: 'error', text: err.message }),
      },
    );
  };

  const reset = () => {
    setStatus(null);
    resetMutation.mutate(setting.key, {
      onSuccess: () => setStatus({ kind: 'ok', text: 'Reset to default.' }),
      onError: (err) => setStatus({ kind: 'error', text: err.message }),
    });
  };

  const meta = (
    <span className="usa-hint display-block margin-top-05" id={hintId}>
      {setting.description}
      {' '}
      <span className="text-base-dark">
        [{SCOPE_LABEL[setting.scope]}
        {setting.isOverridden
          ? ` · default: ${setting.defaultValue ?? ''}`
          : ' · default'}
        {setting.updatedByName ? ` · changed by ${setting.updatedByName}` : ''}]
      </span>
    </span>
  );

  const statusEl = status && (
    <span
      id={statusId}
      className={status.kind === 'error' ? 'usa-error-message' : 'usa-hint text-success'}
      role={status.kind === 'error' ? 'alert' : 'status'}
    >
      {status.text}
    </span>
  );

  const actions = (
    <div className="margin-top-1">
      {setting.dataType !== 'bool' && (
        <button
          type="button"
          className="usa-button usa-button--outline"
          onClick={() => save(draft)}
          disabled={busy || !isDirty}
          aria-label={`Save ${setting.key}`}
        >
          {setMutation.isPending ? 'Saving…' : 'Save'}
        </button>
      )}
      {setting.isOverridden && (
        <button
          type="button"
          className="usa-button usa-button--unstyled margin-left-1"
          onClick={reset}
          disabled={busy}
          aria-label={`Reset ${setting.key} to default`}
        >
          Reset to default
        </button>
      )}
    </div>
  );

  if (setting.dataType === 'bool') {
    const checked = draft.trim().toLowerCase() === 'true';
    return (
      <div className="usa-form-group" data-testid={`setting-row-${setting.key}`}>
        <div className="usa-checkbox">
          <input
            id={inputId}
            className="usa-checkbox__input"
            type="checkbox"
            checked={checked}
            disabled={busy}
            aria-describedby={describedBy}
            onChange={(e) => {
              const next = e.target.checked ? 'true' : 'false';
              setDraft(next);
              save(next);
            }}
          />
          <label className="usa-checkbox__label" htmlFor={inputId}>
            <code>{setting.key}</code>
          </label>
        </div>
        {meta}
        {statusEl}
        {actions}
      </div>
    );
  }

  return (
    <div className="usa-form-group" data-testid={`setting-row-${setting.key}`}>
      <label className="usa-label" htmlFor={inputId}>
        <code>{setting.key}</code>
      </label>
      {meta}
      {statusEl}
      {setting.dataType === 'json' ? (
        <textarea
          id={inputId}
          className={`usa-textarea${status?.kind === 'error' ? ' usa-input--error' : ''}`}
          value={draft}
          disabled={busy}
          aria-describedby={describedBy}
          onChange={(e) => setDraft(e.target.value)}
        />
      ) : (
        <input
          id={inputId}
          className={`usa-input${status?.kind === 'error' ? ' usa-input--error' : ''}`}
          type={setting.dataType === 'int' ? 'number' : 'text'}
          min={setting.dataType === 'int' ? 0 : undefined}
          inputMode={setting.dataType === 'int' ? 'numeric' : undefined}
          value={draft}
          disabled={busy}
          aria-describedby={describedBy}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && isDirty) {
              e.preventDefault();
              save(draft);
            }
          }}
        />
      )}
      {actions}
    </div>
  );
}

export function SiteSettingsSection(): JSX.Element {
  const { data, isLoading, isError } = useSiteSettingsAdmin();
  const groups = useMemo(() => groupByCategory(data?.items ?? []), [data]);

  return (
    <section aria-labelledby="site-settings-heading" className="margin-bottom-6">
      <h2 id="site-settings-heading" className="usa-prose h3">
        Site Settings
      </h2>
      <p className="usa-prose">
        Runtime configuration and feature flags. Every value here is stored in the database and
        applied by the running API as soon as it is saved — no build or deploy is needed.
      </p>

      {isLoading && <p className="usa-prose">Loading settings…</p>}
      {isError && (
        <p className="usa-prose usa-error-message" role="alert">
          Failed to load settings. Please refresh the page.
        </p>
      )}

      {!isLoading && !isError && groups.length === 0 && (
        <p className="usa-prose">
          No settings have been provisioned yet. They are created the first time the API connects
          to the database.
        </p>
      )}

      {groups.map(([category, items]) => (
        <fieldset key={category} className="usa-fieldset margin-top-4" data-testid={`settings-category-${category}`}>
          <legend className="usa-legend usa-legend--large">{category}</legend>
          {CATEGORY_HELP[category] && <p className="usa-hint margin-top-0">{CATEGORY_HELP[category]}</p>}
          {items.map((setting) => (
            <SettingRow key={setting.key} setting={setting} />
          ))}
        </fieldset>
      ))}
    </section>
  );
}
