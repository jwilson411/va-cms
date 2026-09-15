import { useContentTypeDetail } from './useContentTypes';
import { FieldSchemaTable } from './FieldSchemaTable';

interface ContentTypeDetailPanelProps {
  /** Machine name of the selected content type, e.g. "standard_page". */
  typeName: string;
  onBack: () => void;
}

/**
 * Displays the full field schema for a selected content type.
 * Shown when the user clicks a row in the ContentTypeBrowserPage list.
 *
 * AC: shows display name, description, and ordered field list with
 *     field name, type, required flag, and constraints.
 */
export function ContentTypeDetailPanel({
  typeName,
  onBack,
}: ContentTypeDetailPanelProps): JSX.Element {
  const { data, isLoading, isError, error } = useContentTypeDetail(typeName);

  if (isLoading) {
    return (
      <div aria-live="polite" aria-busy="true">
        <p className="usa-prose">Loading schema…</p>
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div
        className="usa-alert usa-alert--error"
        role="alert"
        aria-live="assertive"
      >
        <div className="usa-alert__body">
          <h4 className="usa-alert__heading">Failed to load schema</h4>
          <p className="usa-alert__text">
            {error instanceof Error ? error.message : 'Unknown error.'}
          </p>
        </div>
      </div>
    );
  }

  return (
    <section aria-labelledby="ct-detail-heading">
      <button
        type="button"
        className="usa-button usa-button--unstyled margin-bottom-2"
        onClick={onBack}
        aria-label="Back to content type list"
      >
        ← Back to content types
      </button>

      <h2 id="ct-detail-heading" className="usa-heading-lg">
        {data.displayName}
      </h2>

      {data.description != null && (
        <p className="usa-prose">{data.description}</p>
      )}

      <dl className="usa-summary-box__list margin-bottom-3">
        <div className="display-flex flex-gap-2 margin-bottom-1">
          <dt className="font-body-xs text-bold">Machine name:</dt>
          <dd>
            <code>{data.name}</code>
          </dd>
        </div>
        {data.templateId && (
          <div className="display-flex flex-gap-2 margin-bottom-1">
            <dt className="font-body-xs text-bold">Template:</dt>
            <dd>
              <code>{data.templateId}</code>
            </dd>
          </div>
        )}
        <div className="display-flex flex-gap-2">
          <dt className="font-body-xs text-bold">Workflow:</dt>
          <dd>{data.allowWorkflow ? 'Enabled' : 'Disabled'}</dd>
        </div>
      </dl>

      <h3 className="usa-heading">
        Fields ({data.fields.length})
      </h3>

      {data.fields.length === 0 ? (
        <p className="usa-prose text-base">No fields defined for this type.</p>
      ) : (
        <FieldSchemaTable fields={data.fields} />
      )}
    </section>
  );
}
