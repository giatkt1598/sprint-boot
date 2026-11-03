import { font, radius, sem } from '../theme/tokens';

/** A space's logo, or the first letters of its key until one is uploaded. Decorative: the name sits next to it. */
export function SpaceLogo({ spaceKey, logoUrl, size = 24 }: { spaceKey?: string | null; logoUrl?: string | null; size?: number }) {
  if (logoUrl) {
    return (
      <img
        src={logoUrl}
        alt=""
        width={size}
        height={size}
        className="shrink-0"
        style={{ objectFit: 'cover', borderRadius: radius.control }}
      />
    );
  }

  return (
    <span
      aria-hidden
      className="flex shrink-0 items-center justify-center"
      style={{
        width: size,
        height: size,
        backgroundColor: sem('accent-default'),
        color: sem('text-on-accent'),
        borderRadius: radius.control,
        fontSize: size >= 48 ? font.size.lg : font.size.xs,
        fontWeight: font.weight.semibold,
      }}
    >
      {(spaceKey ?? '').slice(0, 2)}
    </span>
  );
}
