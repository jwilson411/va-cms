/**
 * NavPreviewPanel — renders the menu as it will appear on the USWDS public header.
 * Issue #46 — AC: "Preview button shows menu render before saving."
 *
 * USWDS: usa-nav__primary, usa-nav__submenu
 */

import React from 'react';
import type { NavPreviewItemDto } from './types';

interface NavPreviewPanelProps {
  menuName: string;
  items: NavPreviewItemDto[];
}

export function NavPreviewPanel({
  menuName,
  items,
}: NavPreviewPanelProps): JSX.Element {
  return (
    <section
      className="border border-base padding-2 radius-md"
      aria-label={`Preview of ${menuName}`}
      data-testid="nav-preview-panel"
    >
      <h3 className="margin-top-0 font-sans-sm text-base-dark">
        Preview: {menuName}
      </h3>
      <p className="usa-hint font-sans-3xs">
        Hidden items are shown with reduced opacity.
      </p>

      <nav aria-label={`${menuName} preview`}>
        <ul
          className="usa-nav__primary usa-accordion"
          role="list"
          data-testid="nav-preview-list"
        >
          {items.map((item) => (
            <PreviewItem key={item.id} item={item} />
          ))}
        </ul>
      </nav>
    </section>
  );
}

interface PreviewItemProps {
  item: NavPreviewItemDto;
}

function PreviewItem({ item }: PreviewItemProps): JSX.Element {
  const hasChildren = item.children.length > 0;

  return (
    <li
      className={`usa-nav__primary-item ${!item.isVisible ? 'opacity-50' : ''}`}
      aria-hidden={!item.isVisible}
    >
      {hasChildren ? (
        <>
          <button
            type="button"
            className="usa-accordion__button usa-nav__link"
            aria-expanded="false"
            title={!item.isVisible ? `${item.label} (hidden)` : item.label}
          >
            <span>{item.label}</span>
            {!item.isVisible && (
              <span className="usa-tag usa-tag--gray margin-left-1" aria-hidden="true">
                Hidden
              </span>
            )}
          </button>
          <ul className="usa-nav__submenu" role="list">
            {item.children.map((child) => (
              <SubPreviewItem key={child.id} item={child} />
            ))}
          </ul>
        </>
      ) : (
        <a
          className="usa-nav__link"
          href={item.url || '#'}
          target={item.target}
          rel={item.target === '_blank' ? 'noopener noreferrer' : undefined}
          title={!item.isVisible ? `${item.label} (hidden)` : undefined}
        >
          <span>{item.label}</span>
          {!item.isVisible && (
            <span className="usa-tag usa-tag--gray margin-left-1" aria-hidden="true">
              Hidden
            </span>
          )}
        </a>
      )}
    </li>
  );
}

function SubPreviewItem({ item }: PreviewItemProps): JSX.Element {
  return (
    <li
      className={`usa-nav__submenu-item ${!item.isVisible ? 'opacity-50' : ''}`}
      aria-hidden={!item.isVisible}
    >
      <a
        href={item.url || '#'}
        target={item.target}
        rel={item.target === '_blank' ? 'noopener noreferrer' : undefined}
      >
        <span>{item.label}</span>
        {!item.isVisible && (
          <span className="usa-tag usa-tag--gray margin-left-1" aria-hidden="true">
            Hidden
          </span>
        )}
      </a>
      {item.children.length > 0 && (
        <ul className="usa-nav__submenu" role="list">
          {item.children.map((child) => (
            <SubPreviewItem key={child.id} item={child} />
          ))}
        </ul>
      )}
    </li>
  );
}
