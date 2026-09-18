/**
 * Serialises a value as JSON that is safe to inline inside an HTML <script>
 * element (#172, epic #152).
 *
 * `JSON.stringify` alone is not enough: it leaves `<`, `>` and `&` untouched, so
 * a CMS field containing `</script><script>…` (writable by any ContentOwner or
 * Editor, who are not admins) closes the JSON-LD block and runs on the public
 * page. The HTML parser does not decode entities inside <script>, so the only
 * safe escape is the JSON `\uXXXX` form, which JSON.parse and JSON-LD consumers
 * read back as the original character.
 *
 * U+2028 / U+2029 are also escaped: they are valid in JSON strings but were line
 * terminators in older JavaScript, and a few consumers still choke on them.
 *
 * Use this for anything that ends up in `dangerouslySetInnerHTML` on a <script>
 * (JSON-LD, bootstrap state) — never plain JSON.stringify.
 */

const UNSAFE = /[<>&\u2028\u2029]/g;

const ESCAPES: Record<string, string> = {
  '<': '\\u003c',
  '>': '\\u003e',
  '&': '\\u0026',
  '\u2028': '\\u2028',
  '\u2029': '\\u2029',
};

/** JSON.stringify with `<`, `>`, `&`, U+2028 and U+2029 escaped as `\uXXXX`. */
export function serializeJsonForHtml(value: unknown): string {
  return JSON.stringify(value).replace(UNSAFE, (ch) => ESCAPES[ch]);
}
