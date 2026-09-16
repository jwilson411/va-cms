import '@testing-library/jest-dom';

// jsdom has no layout engine. ProseMirror (TipTap) calls these while scrolling
// the selection into view after a transaction; stub them so editor tests don't
// throw "getClientRects is not a function".
const emptyRect = (): DOMRect =>
  ({ x: 0, y: 0, top: 0, left: 0, bottom: 0, right: 0, width: 0, height: 0, toJSON: () => ({}) }) as DOMRect;
const emptyRectList = (): DOMRectList =>
  ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] }) as unknown as DOMRectList;

if (typeof Range !== 'undefined') {
  Range.prototype.getClientRects ??= emptyRectList;
  Range.prototype.getBoundingClientRect ??= emptyRect;
}
if (typeof Element !== 'undefined') {
  Element.prototype.getClientRects ??= emptyRectList;
}
