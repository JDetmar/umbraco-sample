// Token rules (ARC-05, CMP-04, ES-035): colour pairs the components use meet WCAG 2.2 contrast,
// and component styles use tokens, never raw colours.
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { test } from 'node:test';

const tokens = JSON.parse(readFileSync(new URL('../build/tokens.json', import.meta.url), 'utf8'));

function luminance(hex) {
  const channels = hex.replace('#', '').match(/../g).map((part) => parseInt(part, 16) / 255);
  const [r, g, b] = channels.map((c) => (c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrast(a, b) {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (light + 0.05) / (dark + 0.05);
}

// [foreground, background, minimum ratio, why]
// The focus outline is offset from the element (base.css), so it is drawn against the background or surface,
// never against the call-to-action button's own fill.
const pairs = [
  ['ColorText', 'ColorBackground', 4.5, 'body text'],
  ['ColorText', 'ColorSurface', 4.5, 'text on cards and calls to action'],
  ['ColorTextMuted', 'ColorBackground', 4.5, 'footer and quotes'],
  ['ColorTextMuted', 'ColorSurface', 4.5, 'muted text on cards'],
  ['ColorLink', 'ColorBackground', 4.5, 'links'],
  ['ColorLink', 'ColorSurface', 4.5, 'links on cards'],
  ['ColorBackground', 'ColorAccent', 4.5, 'call-to-action button text'],
  ['ColorBorder', 'ColorBackground', 3, 'card borders (non-text contrast)'],
  ['ColorFocus', 'ColorBackground', 3, 'focus outline'],
  ['ColorFocus', 'ColorSurface', 3, 'focus outline on cards'],
];

for (const [fg, bg, minimum, why] of pairs) {
  test(`${fg} on ${bg} meets ${minimum}:1 (${why})`, () => {
    assert.ok(tokens[fg] && tokens[bg], `missing token ${fg} or ${bg}`);
    const ratio = contrast(tokens[fg], tokens[bg]);
    assert.ok(ratio >= minimum, `${fg} ${tokens[fg]} on ${bg} ${tokens[bg]} is ${ratio.toFixed(2)}:1`);
  });
}

test('contrast calculation matches known WCAG values', () => {
  assert.equal(contrast('#000000', '#ffffff').toFixed(1), '21.0');
  assert.equal(contrast('#777777', '#ffffff').toFixed(2), '4.48');
});

test('stylesheets use colour tokens, not raw colour values', () => {
  const files = ['base.css', ...readdirSync(new URL('../src/components/', import.meta.url)).map((f) => `components/${f}`)];
  const rawColour = /#[0-9a-f]{3,8}\b|\brgba?\(|\bhsla?\(/i;
  for (const file of files) {
    const css = readFileSync(new URL(`../src/${file}`, import.meta.url), 'utf8');
    assert.doesNotMatch(css, rawColour, `${file} contains a raw colour; add a token instead`);
  }
});
