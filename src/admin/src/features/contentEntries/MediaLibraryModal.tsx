/**
 * MediaLibraryModal — USWDS-compliant dialog for selecting media assets (issue #31).
 *
 * AC: "Media insertion opens media library modal"
 *
 * Keyboard accessibility (issue #64):
 *   - Focus is trapped inside the dialog while open (WCAG 2.1 SC 2.1.2).
 *   - First focusable element receives focus on open.
 *   - Escape closes the dialog and restores focus to the trigger element.
 *   - Tab / Shift+Tab cycle within the modal.
 *   - Focus is restored to the element that opened the modal on close.
 *
 * USWDS Modal pattern:
 *   https://designsystem.digital.gov/components/modal/
 */

import React, { useRef } from 'react';
import { useFocusTrap } from '../../hooks/useFocusTrap';

export interface MediaLibraryModalProps {
  /**
   * Called when the user selects a media asset.
   */
  onSelect: (url: string, altText: string) => void;
  /**
   * Called when the user closes the modal without selecting.
   */
  onClose: () => void;
}

/** Placeholder media items shown in the modal until the media API is integrated. */
const PLACEHOLDER_ASSETS = [
  { id: '1', url: '/media/placeholder-hero.jpg', altText: 'Placeholder hero image' },
  { id: '2', url: '/media/placeholder-icon.svg', altText: 'Placeholder icon' },
  { id: '3', url: '/media/placeholder-banner.png', altText: 'Placeholder banner' },
];

/**
 * USWDS-compliant modal dialog for selecting a media asset from the library.
 *
 * Focus is trapped inside the dialog via the useFocusTrap hook, which:
 *   - Focuses the first interactive element on open.
 *   - Cycles Tab / Shift+Tab within the dialog.
 *   - Calls onClose when Escape is pressed.
 *   - Restores the previously-focused element on unmount.
 */
export function MediaLibraryModal({
  onSelect,
  onClose,
}: MediaLibraryModalProps): JSX.Element {
  const dialogRef = useRef<HTMLDivElement>(null);

  // Trap focus inside the dialog; Escape closes it.
  useFocusTrap(dialogRef, { onEscape: onClose });

  return (
    <>
      {/* Overlay — click outside to close */}
      <div
        className="usa-modal-overlay"
        aria-hidden="true"
        onClick={onClose}
        data-testid="media-modal-overlay"
      />

      {/* Dialog */}
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="media-library-modal-heading"
        className="usa-modal"
        data-testid="media-library-modal"
      >
        <div className="usa-modal__content">
          <div className="usa-modal__main">
            <h2
              id="media-library-modal-heading"
              className="usa-modal__heading"
            >
              Media Library
            </h2>

            <p className="usa-hint">
              Select a media asset to insert into the content.
            </p>

            <ul className="usa-list usa-list--unstyled" data-testid="media-asset-list">
              {PLACEHOLDER_ASSETS.map((asset) => (
                <li key={asset.id} className="va-cms-media-asset">
                  <button
                    type="button"
                    className="usa-button usa-button--outline va-cms-media-asset__btn"
                    onClick={() => onSelect(asset.url, asset.altText)}
                    data-testid={`media-asset-${asset.id}`}
                    aria-label={`Insert ${asset.altText}`}
                  >
                    {asset.altText}
                  </button>
                </li>
              ))}
            </ul>
          </div>

          {/* Close button — always last in DOM so Shift+Tab from the first asset
              wraps here, and Tab from this button wraps to the first asset. */}
          <button
            type="button"
            className="usa-button usa-modal__close"
            aria-label="Close media library modal"
            onClick={onClose}
            data-testid="media-modal-close"
          >
            <span aria-hidden="true">✕</span>
          </button>
        </div>
      </div>
    </>
  );
}
