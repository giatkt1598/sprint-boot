import ButtonBase from '@mui/material/ButtonBase';
import { brand, font, motion, radius, sem } from '../theme/tokens';

/** Light colors take dark text and an outline; dark ones take white text. */
function contrast(hex: string) {
  const value = Number.parseInt(hex.slice(1), 16);
  const [r, g, b] = [value >> 16, (value >> 8) & 255, value & 255].map((channel) => {
    const c = channel / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  const luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
  return luminance > 0.45 ? { text: '#1F1F1F', border: sem('border-strong') } : { text: '#FFFFFF', border: hex };
}

/**
 * The sign-in button of an OpenID Connect provider, in the logo and color an admin chose; without
 * a color it keeps our secondary look. The same component draws the admin's preview.
 */
export function CustomSignInButton({ href, onClick, label, logoUrl, color }: {
  href?: string;
  /** For providers signed into on our own page, such as LDAP, instead of a link away. */
  onClick?: () => void;
  label: string;
  logoUrl?: string | null;
  color?: string | null;
}) {
  const custom = color ? { background: color, ...contrast(color) } : null;

  return (
    <ButtonBase
      component={href ? 'a' : onClick ? 'button' : 'span'}
      href={href}
      onClick={onClick}
      sx={{
        position: 'relative',
        display: 'flex',
        width: '100%',
        height: brand.signInHeight,
        paddingInline: '12px',
        justifyContent: 'center',
        backgroundColor: custom?.background ?? sem('bg-surface'),
        color: custom?.text ?? sem('text-primary'),
        border: `1px solid ${custom?.border ?? sem('border-strong')}`,
        borderRadius: `${radius.control}px`,
        fontSize: font.size.md,
        fontWeight: font.weight.medium,
        lineHeight: 1.4,
        transition: `filter ${motion.fast}, background-color ${motion.fast}`,
        '&:hover': custom ? { filter: 'brightness(0.94)' } : { backgroundColor: sem('bg-hover') },
        '&.Mui-focusVisible': { outline: `2px solid ${sem('border-focus')}`, outlineOffset: 2 },
      }}
    >
      {logoUrl && (
        <img src={logoUrl} alt="" width={18} height={18} style={{ position: 'absolute', left: 12, objectFit: 'contain' }} />
      )}
      <span className="truncate" style={{ paddingInline: logoUrl ? 24 : 0 }}>{label}</span>
    </ButtonBase>
  );
}
