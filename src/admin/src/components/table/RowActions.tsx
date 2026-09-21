import React from 'react';

export interface RowActionsProps {
  children: React.ReactNode;
  /** Extra utility classes for the wrapper (e.g. alignment overrides). */
  className?: string;
}

/**
 * Consistent layout for the actions cell of an admin table row.
 *
 * Without this, unstyled USWDS buttons sit flush against each other and read as
 * one control. `.va-cms-row-actions` gives them a real gap and lets them wrap
 * on narrow screens (see styles/admin.scss).
 */
export function RowActions({ children, className }: RowActionsProps): JSX.Element {
  return (
    <div className={className ? `va-cms-row-actions ${className}` : 'va-cms-row-actions'}>
      {children}
    </div>
  );
}
