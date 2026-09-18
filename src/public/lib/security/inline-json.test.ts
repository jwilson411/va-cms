/**
 * #172: JSON inlined into a <script> must not be able to close the element.
 */
import { serializeJsonForHtml } from './inline-json';

describe('serializeJsonForHtml', () => {
  it('escapes a </script> payload so the element cannot be closed', () => {
    const out = serializeJsonForHtml({ headline: '</script><script>alert(1)</script>' });
    expect(out).not.toContain('<');
    expect(out).not.toContain('>');
    expect(out).toBe('{"headline":"\\u003c/script\\u003e\\u003cscript\\u003ealert(1)\\u003c/script\\u003e"}');
  });

  it('escapes & and the U+2028 / U+2029 line separators', () => {
    const out = serializeJsonForHtml({ t: 'a & b\u2028c\u2029d' });
    expect(out).toBe('{"t":"a \\u0026 b\\u2028c\\u2029d"}');
  });

  it('round-trips through JSON.parse unchanged', () => {
    const input = {
      '@context': 'https://schema.org',
      headline: 'Tom & Jerry <3 "quotes" \u2028 done',
      nested: { list: ['<', '>', '&'], n: 1, b: false, z: null },
    };
    expect(JSON.parse(serializeJsonForHtml(input))).toEqual(input);
  });

  it('leaves ordinary JSON alone', () => {
    expect(serializeJsonForHtml({ a: 1, b: 'plain', c: [true, null] })).toBe(JSON.stringify({ a: 1, b: 'plain', c: [true, null] }));
  });
});
