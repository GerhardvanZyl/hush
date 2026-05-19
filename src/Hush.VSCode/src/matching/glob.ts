/**
 * Tiny glob: `*` = any chars, `?` = one char, `|` at top level = alternation.
 * Case-insensitive. Mirrors `src/Hush.Core/Matching/GlobPattern.cs`
 * so rules behave identically on both hosts.
 */

const cache = new Map<string, RegExp>();

export function globMatch(glob: string | null | undefined, text: string | null | undefined): boolean {
  if (!glob) return true;
  if (text == null) return false;
  let regex = cache.get(glob);
  if (!regex) {
    regex = new RegExp('^(?:' + translate(glob) + ')$', 'i');
    cache.set(glob, regex);
  }
  return regex.test(text);
}

function translate(glob: string): string {
  return glob.split('|').map(translateOne).join('|');
}

function translateOne(glob: string): string {
  let out = '';
  for (const c of glob) {
    if (c === '*') out += '.*';
    else if (c === '?') out += '.';
    else if ('\\.+()[]{}^$'.includes(c)) out += '\\' + c;
    else out += c;
  }
  return out;
}
