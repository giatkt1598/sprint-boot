import { afterEach, describe, expect, it } from 'vitest';
import { clearSignedIn, markSignedIn, SIGNED_IN_COOKIE } from './signedInHint';

describe('signed-in hint cookie', () => {
  afterEach(() => clearSignedIn());

  it('is set for the whole site and cleared again', () => {
    markSignedIn();
    expect(document.cookie).toContain(`${SIGNED_IN_COOKIE}=1`);

    clearSignedIn();
    expect(document.cookie).not.toContain(SIGNED_IN_COOKIE);
  });

  it('has a name nginx can read as a variable', () => {
    expect(SIGNED_IN_COOKIE).toMatch(/^[a-z_]+$/);
  });
});
