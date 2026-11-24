// Token rules (ARC-05, CMP-01, ES-035): colour pairs the components use meet WCAG 2.2 contrast,
// and the built stylesheet uses tokens, never raw colours.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { coloursInUse, rawColourDeclarations } from '../scripts/colour-policy.mjs';

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

const builtCss = readFileSync(new URL('../../src/UmbracoSample.Web/wwwroot/dist/site.css', import.meta.url), 'utf8');

test('colour tokens are opaque six-digit hex values', () => {
  for (const [name, value] of Object.entries(tokens).filter(([key]) => key.startsWith('Color'))) {
    assert.match(value, /^#[0-9a-f]{6}$/i, `${name} must be #rrggbb so contrast is computed on the real colour`);
  }
});

test('the built stylesheet sets colours only through tokens', () => {
  assert.deepEqual(rawColourDeclarations(builtCss), []);
});

test('the colour policy catches every kind of raw colour', () => {
  for (const raw of ['color:red', 'color:#fff', 'background:oklch(70% .1 200)', 'border:1px solid rgb(0 0 0)', 'outline:2px solid hsl(0 0% 0%)', 'box-shadow:0 0 2px color(srgb 1 0 0)', 'color:lab(50% 0 0)', 'color:hwb(0 0% 0%)', 'background-color:white', '--c:red', 'background-image:linear-gradient(red,blue)', 'border-image:linear-gradient(red,blue) 1']) {
    assert.equal(rawColourDeclarations(`.x{${raw}}`).length, 1, `missed ${raw}`);
  }
  // Only the first :root (the generated tokens) is exempt; a hand-written one later is checked.
  assert.equal(rawColourDeclarations(':root{--color-text:#111}.x{color:var(--color-text)}:root{color:red}').length, 1, 'missed a second :root');
  for (const ok of ['color:var(--color-text)', 'border:1px solid var(--color-border)', 'outline:var(--focus-width) solid var(--color-focus)', 'background:transparent', 'color:currentcolor', 'text-decoration:underline', '--gap:1rem', '--c:var(--color-text)', 'background-image:linear-gradient(to right,var(--color-surface),var(--color-background))']) {
    assert.deepEqual(rawColourDeclarations(`.x{${ok}}`), [], `wrongly flagged ${ok}`);
  }
});

test('every colour the stylesheet uses for text or background is covered by a contrast pair', () => {
  const covered = new Set(pairs.flatMap(([fg, bg]) => [fg, bg]));
  const toTokenName = (cssName) => cssName.replace(/(^|-)([a-z])/g, (_, __, c) => c.toUpperCase());
  const uncovered = [...coloursInUse(builtCss)].map(toTokenName).filter((name) => !covered.has(name));
  assert.deepEqual(uncovered, [], 'add a contrast pair for each colour used as text or background');
});
