import React from 'react';

/** USWDS icon sizes (`usa-icon--size-*`); omitted uses the 1em default. */
export type IconSize = 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9;

export interface IconProps {
  /** Sprite symbol id, e.g. "grid_view" (see uswds/img/sprite.svg). */
  name: string;
  size?: IconSize;
  className?: string;
  /**
   * Accessible name. When omitted the icon is decorative and hidden from
   * assistive tech — the surrounding control must carry the label.
   */
  title?: string;
}

/**
 * Renders a USWDS sprite icon (design system rule: use the `usa-icon` sprite,
 * never ad-hoc SVGs or emoji).
 */
export function Icon({ name, size, className, title }: IconProps): JSX.Element {
  const classes = ['usa-icon', size ? `usa-icon--size-${size}` : '', className]
    .filter(Boolean)
    .join(' ');
  const decorative = title == null;

  return (
    <svg
      className={classes}
      aria-hidden={decorative ? 'true' : undefined}
      aria-label={title}
      role={decorative ? undefined : 'img'}
      focusable="false"
    >
      <use href={`/uswds/img/sprite.svg#${name}`} />
    </svg>
  );
}
