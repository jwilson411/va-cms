import type { FieldSchemaDto } from './types';

interface FieldSchemaTableProps {
  fields: FieldSchemaDto[];
}

/**
 * Renders a USWDS Table showing a content type's ordered field schema.
 *
 * Columns: Field Name | Type | Required | Constraints
 *
 * USWDS reference: https://designsystem.digital.gov/components/table/
 */
export function FieldSchemaTable({ fields }: FieldSchemaTableProps): JSX.Element {
  return (
    <div className="usa-table-container--scrollable" tabIndex={0}>
      <table className="usa-table usa-table--borderless width-full">
        <caption className="usa-sr-only">Field schema</caption>
        <thead>
          <tr>
            <th scope="col">Field Name</th>
            <th scope="col">Type</th>
            <th scope="col">Required</th>
            <th scope="col">Constraints</th>
          </tr>
        </thead>
        <tbody>
          {fields.map((field) => (
            <tr key={field.name}>
              <td>
                <code>{field.name}</code>
                {field.label !== field.name && (
                  <span className="display-block font-body-3xs text-base-dark">
                    {field.label}
                  </span>
                )}
              </td>
              <td>
                <span className="usa-tag usa-tag--big">{field.type}</span>
              </td>
              <td>
                {field.required ? (
                  <span aria-label="Yes" className="text-success-dark">
                    ✓ Yes
                  </span>
                ) : (
                  <span aria-label="No" className="text-base">
                    No
                  </span>
                )}
              </td>
              <td>
                {field.maxLength != null ? (
                  <span>Max {field.maxLength} chars</span>
                ) : (
                  <span className="text-base">—</span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
