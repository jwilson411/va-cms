import { useState } from 'react';
import { useContentTypes } from '../features/contentTypes/useContentTypes';
import { ContentTypeDetailPanel } from '../features/contentTypes/ContentTypeDetailPanel';
import type { ContentTypeSummaryDto } from '../features/contentTypes/types';
import { SortableHeader, useSortableRows } from '../components/table';

type ContentTypeSortKey = 'displayName' | 'name' | 'fieldCount' | 'allowWorkflow';

function contentTypeSortValue(
  type: ContentTypeSummaryDto,
  key: ContentTypeSortKey,
): string | number | boolean {
  return type[key];
}

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
      <main id="main-content" className="padding-top-3">
        <ContentTypeDetailPanel
          typeName={selectedTypeName}
          onBack={() => setSelectedTypeName(null)}
        />
      </main>
    );
  }

  return (
    <main id="main-content" className="padding-top-3">
      <h1 className="font-heading-xl">Content Types</h1>
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
  const {
    rows: sortedTypes,
    sortKey,
    sortDirection,
    toggleSort,
  } = useSortableRows<ContentTypeSummaryDto, ContentTypeSortKey>(types, {
    initialKey: 'displayName',
    getValue: contentTypeSortValue,
  });

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
            <SortableHeader label="Display Name" field="displayName" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
            <SortableHeader label="Machine Name" field="name" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
            <SortableHeader label="Fields" field="fieldCount" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
            <SortableHeader label="Workflow" field="allowWorkflow" currentSortBy={sortKey} currentSortDir={sortDirection} onSort={toggleSort} />
            <th scope="col">
              <span className="usa-sr-only">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {sortedTypes.map((type) => (
            <tr key={type.name}>
              <td>
                <button
                  type="button"
                  className="usa-button usa-button--unstyled"
                  onClick={() => onSelect(type.name)}
                  aria-label={`Open ${type.displayName}`}
                >
                  {type.displayName}
                </button>
              </td>
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
