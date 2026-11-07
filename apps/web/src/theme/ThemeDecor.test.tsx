import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { MotionChoice } from './appearance';
import { DecorLayer } from './ThemeDecor';
import { themeById, type DecorProps, type ThemeDefinition } from './themes';

const animated: ThemeDefinition = {
  ...themeById('classic'),
  id: 'test-animated',
  decor: () => Promise.resolve({ default: ({ still, paused }: DecorProps) => <span>{`still:${still} paused:${paused}`}</span> }),
};

const show = (motion: MotionChoice, { osReduced = false, hidden = false } = {}) =>
  render(<DecorLayer theme={animated} motion={motion} osReduced={osReduced} hidden={hidden} />);

describe('DecorLayer', () => {
  afterEach(cleanup);

  it('animates at full motion', async () => {
    show('full');
    expect(await screen.findByText('still:false paused:false')).toBeTruthy();
    expect(screen.getByTestId('theme-decor').hasAttribute('data-paused')).toBe(false);
  });

  it('holds still when the person or the device asks for less motion', async () => {
    show('reduced');
    expect(await screen.findByText('still:true paused:false')).toBeTruthy();
    expect(screen.getByTestId('theme-decor').hasAttribute('data-paused')).toBe(true);
  });

  it('follows the device setting even at full motion', async () => {
    show('full', { osReduced: true });
    expect(await screen.findByText('still:true paused:false')).toBeTruthy();
  });

  it('pauses while the tab is hidden', async () => {
    show('full', { hidden: true });
    expect(await screen.findByText('still:false paused:true')).toBeTruthy();
    expect(screen.getByTestId('theme-decor').hasAttribute('data-paused')).toBe(true);
  });

  it('stays out when motion is off or the theme has no decor', () => {
    const { container } = show('off');
    expect(container.innerHTML).toBe('');
    const plain = render(<DecorLayer theme={themeById('classic')} motion="full" osReduced={false} hidden={false} />);
    expect(plain.container.innerHTML).toBe('');
  });
});
