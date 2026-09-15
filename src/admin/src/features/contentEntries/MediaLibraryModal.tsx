/**
 * MediaLibraryModal — opens a USWDS dialog for selecting media assets (issue #31).
 *
 * AC: "Media insertion opens media library modal"
 *
 * This is a functional stub that renders a USWDS modal dialog with a placeholder
 * media asset list. A future story (#65 or #66) will wire in the live media API.
 */

import React, { useEffect, useRef } from 'react';

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
 * Traps focus while open. Closes on Escape key or overlay click.
 */
export function MediaLibraryModal({
  onSelect,
  onClose,
}: MediaLibraryModalProps): JSX.Element {
  const firstFocusRef = useRef<HTMLButtonElement | null>(null);
  const dialogRef = useRef<HTMLDivElement | null>(null);

  // Focus the first focusable element on mount
  useEffect(() => {
    firstFocusRef.current?.focus();
  }, []);

  // Close on Escape key
  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        onClose();
      }
    }
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

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
                    ref={asset.id === '1' ? firstFocusRef : undefined}
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
