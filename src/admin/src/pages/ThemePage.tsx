/**
 * ThemePage — USWDS / VA theme smoke-test page (issue #17, BRD FR-AUTH-01).
 *
 * Renders a handful of USWDS components so the compiled theme can be checked
 * in a browser: VA Blue (#003e73) on primary buttons/links, VA Gold (#f9c642)
 * on accent-warm, and Public Sans as the type face. Not linked from the nav —
 * reach it at /admin/theme.
 *
 * The swatch hexes here are documentation only; the real values live in
 * src/theme/uswds/_va-settings.scss.
 */

const SWATCHES = [
  { token: 'primary', hex: '#003e73', label: 'VA Blue', className: 'bg-primary text-white' },
  { token: 'primary-vivid', hex: '#0071bb', label: 'VA primary', className: 'bg-primary-vivid text-white' },
  { token: 'primary-dark', hex: '#112e51', label: 'VA primary-darkest', className: 'bg-primary-dark text-white' },
  { token: 'accent-warm', hex: '#f9c642', label: 'VA Gold', className: 'bg-accent-warm text-ink' },
  { token: 'accent-warm-dark', hex: '#fdb81e', label: 'VA gold', className: 'bg-accent-warm-dark text-ink' },
  { token: 'secondary', hex: '#e31c3d', label: 'VA secondary', className: 'bg-secondary text-white' },
] as const;

export function ThemePage(): JSX.Element {
  return (
    <main id="main-content" data-testid="theme-page">
      <h1>USWDS theme check</h1>
      <p className="usa-intro">
        This page renders USWDS 3 components compiled with the VA theme tokens.
        Body and headings should render in Public Sans.
      </p>

      <h2>Buttons</h2>
      <ul className="usa-button-group">
        <li className="usa-button-group__item">
          <button type="button" className="usa-button">Primary (VA Blue)</button>
        </li>
        <li className="usa-button-group__item">
          <button type="button" className="usa-button usa-button--secondary">Secondary</button>
        </li>
        <li className="usa-button-group__item">
          <button type="button" className="usa-button usa-button--accent-warm">Accent warm (VA Gold)</button>
        </li>
        <li className="usa-button-group__item">
          <button type="button" className="usa-button usa-button--outline">Outline</button>
        </li>
        <li className="usa-button-group__item">
          <button type="button" className="usa-button usa-button--unstyled">Unstyled</button>
        </li>
      </ul>

      <h2>Alert</h2>
      <div className="usa-alert usa-alert--info" role="region" aria-label="Information">
        <div className="usa-alert__body">
          <h3 className="usa-alert__heading">Theme tokens</h3>
          <p className="usa-alert__text">
            Links such as <a href="#main-content" className="usa-link">this one</a> use the primary token.
          </p>
        </div>
      </div>

      <h2>Color swatches</h2>
      <ul className="usa-list usa-list--unstyled grid-row grid-gap-1" data-testid="theme-swatches">
        {SWATCHES.map(({ token, hex, label, className }) => (
          <li key={token} className={`tablet:grid-col-4 padding-2 radius-md ${className}`}>
            <span className="text-bold font-mono-sm">{token}</span>
            <br />
            <span className="font-mono-xs">{hex}</span> — {label}
          </li>
        ))}
      </ul>

      <h2>Typography</h2>
      <p className="font-sans-lg">Public Sans — the quick brown fox jumps over the lazy dog.</p>
      <p className="font-mono-sm">Roboto Mono — const theme = &quot;va&quot;;</p>
    </main>
  );
}
