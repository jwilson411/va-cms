/**
 * MediaLibraryModal tests — keyboard navigation and USWDS modal accessibility.
 *
 * Issue #64: Complete keyboard navigation audit across admin.
 *
 * Covers:
 *   - Modal renders with role="dialog" and aria-modal="true"
 *   - aria-labelledby points to heading id
 *   - All asset buttons are present and have aria-label
 *   - Escape key calls onClose
 *   - Close button calls onClose
 *   - Asset select button calls onSelect with correct args
 *   - Overlay click calls onClose
 */

import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { MediaLibraryModal } from './MediaLibraryModal';

describe('MediaLibraryModal', () => {
  const onSelect = vi.fn();
  const onClose = vi.fn();

  beforeEach(() => {
    onSelect.mockReset();
    onClose.mockReset();
  });

  function renderModal() {
    return render(<MediaLibraryModal onSelect={onSelect} onClose={onClose} />);
  }

  it('renders with role=dialog and aria-modal', () => {
    renderModal();
    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeInTheDocument();
    expect(dialog).toHaveAttribute('aria-modal', 'true');
  });

  it('has aria-labelledby pointing to the heading', () => {
    renderModal();
    const dialog = screen.getByRole('dialog');
    const headingId = dialog.getAttribute('aria-labelledby');
    expect(headingId).toBeTruthy();
    const heading = document.getElementById(headingId!);
    expect(heading).toBeInTheDocument();
    expect(heading?.textContent).toBe('Media Library');
  });

  it('renders all placeholder asset buttons with aria-label', () => {
    renderModal();
    // 3 assets + 1 close button = 4 buttons total
    const assetButtons = [
      screen.getByTestId('media-asset-1'),
      screen.getByTestId('media-asset-2'),
      screen.getByTestId('media-asset-3'),
    ];
    assetButtons.forEach((btn) => {
      expect(btn).toHaveAttribute('aria-label');
      expect(btn.getAttribute('aria-label')!.startsWith('Insert')).toBe(true);
    });
  });

  it('calls onSelect with url and altText when an asset button is clicked', () => {
    renderModal();
    fireEvent.click(screen.getByTestId('media-asset-1'));
    expect(onSelect).toHaveBeenCalledWith(
      '/media/placeholder-hero.jpg',
      'Placeholder hero image',
    );
  });

  it('calls onClose when close button is clicked', () => {
    renderModal();
    fireEvent.click(screen.getByTestId('media-modal-close'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when overlay is clicked', () => {
    renderModal();
    fireEvent.click(screen.getByTestId('media-modal-overlay'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when Escape is pressed', () => {
    renderModal();
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('close button has descriptive aria-label', () => {
    renderModal();
    const closeBtn = screen.getByTestId('media-modal-close');
    expect(closeBtn).toHaveAttribute('aria-label', 'Close media library modal');
  });
});
