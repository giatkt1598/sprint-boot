import { beforeEach, describe, expect, it } from 'vitest';
import { appLocationOr, rememberAppLocation } from './returnTo';

describe('returnTo', () => {
  beforeEach(() => sessionStorage.clear());

  it('falls back when no app page was seen in this tab', () => {
    expect(appLocationOr('/spaces/SB/issues')).toBe('/spaces/SB/issues');
  });

  it('returns the last app page seen', () => {
    rememberAppLocation('/spaces/SB/board?sprint=1');
    rememberAppLocation('/spaces/SB/backlog');
    expect(appLocationOr('/')).toBe('/spaces/SB/backlog');
  });
});
