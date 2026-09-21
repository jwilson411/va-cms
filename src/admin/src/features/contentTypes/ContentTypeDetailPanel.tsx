import { useContentTypeDetail } from './useContentTypes';
import { FieldSchemaTable } from './FieldSchemaTable';
import { Icon } from '../../components/Icon';

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
        <Icon name="arrow_back" size={3} className="margin-right-1" />
        Back to content types
      </button>

      <h1 id="ct-detail-heading" className="font-heading-xl margin-top-0">
        {data.displayName}
      </h1>

      {/* USWDS Summary Box: description + schema metadata at a glance. */}
      <div
        className="usa-summary-box margin-bottom-4"
        role="region"
        aria-labelledby="ct-summary-heading"
      >
        <div className="usa-summary-box__body">
          <h2 id="ct-summary-heading" className="usa-summary-box__heading">
            Overview
          </h2>
          <div className="usa-summary-box__text">
            {data.description != null && (
              <p className="margin-top-0">{data.description}</p>
            )}

            <dl className="margin-0">
              <dt className="font-body-2xs text-bold text-uppercase text-base-dark">
                Machine name
              </dt>
              <dd className="margin-0 margin-bottom-1">
                <code>{data.name}</code>
              </dd>

              {data.templateId && (
                <>
                  <dt className="font-body-2xs text-bold text-uppercase text-base-dark">
                    Template
                  </dt>
                  <dd className="margin-0 margin-bottom-1">
                    <code>{data.templateId}</code>
                  </dd>
                </>
              )}

              <dt className="font-body-2xs text-bold text-uppercase text-base-dark">
                Workflow
              </dt>
              <dd className="margin-0">
                {data.allowWorkflow ? 'Enabled' : 'Disabled'}
              </dd>
            </dl>
          </div>
        </div>
      </div>

      <h2 className="font-heading-lg margin-bottom-2">
        Fields ({data.fields.length})
      </h2>

      {data.fields.length === 0 ? (
        <p className="usa-prose text-base">No fields defined for this type.</p>
      ) : (
        <FieldSchemaTable fields={data.fields} />
      )}
    </section>
  );
}
