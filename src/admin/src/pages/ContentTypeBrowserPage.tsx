import { useState } from 'react';
import { useContentTypes } from '../features/contentTypes/useContentTypes';
import { ContentTypeDetailPanel } from '../features/contentTypes/ContentTypeDetailPanel';
import type { ContentTypeSummaryDto } from '../features/contentTypes/types';

/**
 * Admin content type browser page — /admin/content-types
 *
 * AC (FR-SCHEMA-06 / issue #26):
 * - Lists all registered content types in a USWDS Table
 * - Clicking a row shows the type's display name, description, and ordered field list
 */
export function ContentTypeBrowserPage(): JSX.Element {
  const [selectedTypeName, setSelectedTypeName] = useState<string | null>(null);
  const { data, isLoading, isError, error } = useContentTypes();

  if (selectedTypeName != null) {
    return (
      <main id="main-content" className="grid-container padding-top-3">
        <ContentTypeDetailPanel
          typeName={selectedTypeName}
          onBack={() => setSelectedTypeName(null)}
        />
      </main>
    );
  }

  return (
    <main id="main-content" className="grid-container padding-top-3">
      <h1 className="usa-heading-xl">Content Types</h1>
      <p className="usa-prose">
        All content types registered in the CMS. Click a type to view its field schema.
      </p>

      {isLoading && (
        <div aria-live="polite" aria-busy="true">
          <p className="usa-prose">Loading content types…</p>
        </div>
      )}

      {isError && (
        <div
          className="usa-alert usa-alert--error"
          role="alert"
          aria-live="assertive"
        >
          <div className="usa-alert__body">
            <h4 className="usa-alert__heading">Failed to load content types</h4>
            <p className="usa-alert__text">
              {error instanceof Error ? error.message : 'Unknown error.'}
            </p>
          </div>
        </div>
      )}

      {data != null && (
        <ContentTypeListTable
          types={data}
          onSelect={(name) => setSelectedTypeName(name)}
        />
      )}
    </main>
  );
}

// ── Sub-components ────────────────────────────────────────────────────────────

interface ContentTypeListTableProps {
  types: ContentTypeSummaryDto[];
  onSelect: (name: string) => void;
}

/**
 * USWDS Table listing all registered content types.
 * Each row is clickable to drill into the field schema viewer.
 */
function ContentTypeListTable({
  types,
  onSelect,
}: ContentTypeListTableProps): JSX.Element {
  if (types.length === 0) {
    return (
      <p className="usa-prose text-base">
        No content types are registered.
      </p>
    );
  }

  return (
    <div className="usa-table-container--scrollable" tabIndex={0}>
      <table className="usa-table usa-table--borderless width-full">
        <caption className="usa-sr-only">Registered content types</caption>
        <thead>
          <tr>
            <th scope="col">Display Name</th>
            <th scope="col">Machine Name</th>
            <th scope="col">Fields</th>
            <th scope="col">Workflow</th>
            <th scope="col">
              <span className="usa-sr-only">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {types.map((type) => (
            <tr key={type.name}>
              <td>{type.displayName}</td>
              <td>
                <code>{type.name}</code>
              </td>
              <td>{type.fieldCount}</td>
              <td>{type.allowWorkflow ? 'Yes' : 'No'}</td>
              <td>
                <button
                  type="button"
                  className="usa-button usa-button--unstyled"
                  onClick={() => onSelect(type.name)}
                  aria-label={`View schema for ${type.displayName}`}
                >
                  View schema
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
