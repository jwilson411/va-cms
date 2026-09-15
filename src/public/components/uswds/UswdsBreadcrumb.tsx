/**
 * UswdsBreadcrumb — USWDS Breadcrumb component for VA public pages.
 *
 * Renders the usa-breadcrumb nav using USWDS 3.x markup conventions.
 * The final item is the current page (aria-current="page", not a link).
 *
 * BRD NFR-A11Y-02: USWDS components only, no custom styles.
 * BRD Issue #58: Required on Standard Page template.
 */

import React from 'react';

export interface BreadcrumbItem {
  /** Display label for this crumb */
  label: string;
  /** Link destination — omit on the last (current) item */
  href?: string;
}

export interface UswdsBreadcrumbProps {
  /** Ordered list of breadcrumb items. Last item = current page. */
  items: BreadcrumbItem[];
}

export function UswdsBreadcrumb({
  items,
}: UswdsBreadcrumbProps): React.ReactElement | null {
  if (!items || items.length === 0) return null;

  return (
    <nav className="usa-breadcrumb" aria-label="Breadcrumbs">
      <ol className="usa-breadcrumb__list">
        {items.map((item, index) => {
          const isLast = index === items.length - 1;
          return (
            <li
              key={`${item.label}-${index}`}
              className={`usa-breadcrumb__list-item${isLast ? ' usa-current' : ''}`}
              aria-current={isLast ? 'page' : undefined}
            >
              {isLast ? (
                <span>{item.label}</span>
              ) : (
                <a href={item.href ?? '#'} className="usa-breadcrumb__link">
                  <span>{item.label}</span>
                </a>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
