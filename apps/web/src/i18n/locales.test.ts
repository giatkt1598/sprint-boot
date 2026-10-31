import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import vi from './locales/vi.json';

const keys = (value: object, prefix = ''): string[] =>
  Object.entries(value).flatMap(([key, child]) =>
    typeof child === 'object' ? keys(child, `${prefix}${key}.`) : [`${prefix}${key}`],
  );

describe('locale files', () => {
  it('define the same keys', () => {
    expect(keys(vi).sort()).toEqual(keys(en).sort());
  });
});
