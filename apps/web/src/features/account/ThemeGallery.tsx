import ButtonBase from '@mui/material/ButtonBase';
import { Check } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Badge } from '../../components/Badge';
import { Tabs } from '../../components/Tabs';
import { THEME_CATEGORIES, THEMES, type ThemeCategory, type ThemeDefinition, type ThemeId } from '../../theme/themes';
import { font, radius, sem, shadow, type Accent } from '../../theme/tokens';

type Filter = ThemeCategory | 'all';

const tagsOf = (theme: ThemeDefinition) =>
  [theme.decor && 'animated', theme.gradient && 'gradient', theme.base === 'dark' && 'dark'].filter(Boolean) as string[];

/**
 * Every theme as a card that shows it in miniature. The miniature carries the theme's own attribute, so it renders
 * with that theme's variables whatever the app is wearing.
 */
export function ThemeGallery({ value, accent, onChange }: { value: ThemeId; accent: Accent; onChange: (theme: ThemeId) => void }) {
  const { t } = useTranslation();
  const [filter, setFilter] = useState<Filter>('all');
  const categories = THEME_CATEGORIES.filter((category) => THEMES.some((theme) => theme.category === category));
  const shown = THEMES.filter((theme) => filter === 'all' || theme.category === filter);

  return (
    <div className="flex flex-col gap-3">
      {categories.length > 1 && (
        <div style={{ borderBottom: `1px solid ${sem('border-default')}` }}>
          <Tabs
            label={t('account.preferences.themeCategories')}
            items={(['all', ...categories] as Filter[]).map((category) => ({ value: category, label: t(`account.preferences.category.${category}`) }))}
            value={filter}
            onChange={(next) => setFilter(next as Filter)}
          />
        </div>
      )}
      <div role="radiogroup" aria-label={t('account.preferences.theme')} className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
        {shown.map((theme) => (
          <ThemeCard key={theme.id} theme={theme} accent={accent} selected={theme.id === value} onSelect={() => onChange(theme.id)} />
        ))}
      </div>
    </div>
  );
}

function ThemeCard({ theme, accent, selected, onSelect }: { theme: ThemeDefinition; accent: Accent; selected: boolean; onSelect: () => void }) {
  const { t } = useTranslation();
  const name = t(`account.preferences.themeName.${theme.id}`);
  const tags = tagsOf(theme);

  return (
    <ButtonBase
      role="radio"
      aria-checked={selected}
      aria-label={name}
      onClick={onSelect}
      className="flex flex-col items-stretch overflow-hidden text-left"
      sx={{
        border: `1px solid ${sem('border-default')}`,
        borderRadius: `${radius.card}px`,
        backgroundColor: sem('bg-surface'),
        boxShadow: selected ? `0 0 0 2px ${sem('border-focus')}` : 'none',
        '&:hover': { borderColor: sem('border-strong') },
      }}
    >
      <Miniature theme={theme} accent={accent} />
      <span className="flex flex-col gap-1 p-2" style={{ borderTop: `1px solid ${sem('border-default')}` }}>
        <span className="flex items-center justify-between gap-2" style={{ fontSize: font.size.md, fontWeight: font.weight.medium, color: sem('text-primary') }}>
          <span className="truncate">{name}</span>
          {selected && (
            <span aria-hidden className="flex size-5 shrink-0 items-center justify-center" style={{ borderRadius: radius.full, backgroundColor: sem('accent-default'), color: sem('text-on-accent') }}>
              <Check size={12} strokeWidth={2} />
            </span>
          )}
        </span>
        {tags.length > 0 && (
          <span className="flex flex-wrap gap-1">
            {tags.map((tag) => (
              <Badge key={tag} tone="subtle">{t(`account.preferences.themeTag.${tag}`)}</Badge>
            ))}
          </span>
        )}
      </span>
    </ButtonBase>
  );
}

const line = (token: Parameters<typeof sem>[0], width: string, height = 4) => ({
  display: 'block',
  width,
  height,
  borderRadius: radius.full,
  backgroundColor: sem(token),
});

/** A top bar, a sidebar with one selected item, and a card with statuses and a button: enough to read a theme. */
function Miniature({ theme, accent }: { theme: ThemeDefinition; accent: Accent }) {
  return (
    <span
      aria-hidden
      data-app-theme={theme.id}
      data-accent={theme.accents ? accent : undefined}
      className="flex h-24 flex-col overflow-hidden"
      style={{ background: 'var(--sb-page-bg)' }}
    >
      <span className="sb-surface flex h-3 shrink-0 items-center px-1.5" style={{ backgroundColor: sem('bg-surface'), borderBottom: `1px solid ${sem('border-default')}` }}>
        <span style={line('text-tertiary', '20%', 3)} />
      </span>
      <span className="flex min-h-0 flex-1">
        <span className="sb-surface flex w-1/4 flex-col gap-1 p-1.5" style={{ backgroundColor: sem('bg-surface'), borderRight: `1px solid ${sem('border-default')}` }}>
          <span style={line('bg-selected', '100%', 6)} />
          <span style={line('bg-subtle', '80%')} />
          <span style={line('bg-subtle', '90%')} />
          <span style={line('bg-subtle', '70%')} />
        </span>
        <span className="flex min-w-0 flex-1 flex-col gap-1.5 p-2" style={{ background: 'var(--sb-content-bg)' }}>
          <span style={{ ...line('text-primary', '45%', 5), fontFamily: 'var(--sb-heading-font)' }} />
          <span className="flex flex-col gap-1 p-1.5" style={{ backgroundColor: sem('bg-surface'), boxShadow: shadow.card, borderRadius: 4, border: `1px solid ${sem('border-default')}` }}>
            <span style={line('text-secondary', '75%', 3)} />
            <span className="flex gap-1">
              <span style={line('status-progress-bg', '28%', 6)} />
              <span style={line('status-done-bg', '20%', 6)} />
            </span>
          </span>
          <span className="mt-auto self-end" style={{ ...line('accent-default', '32%', 8), borderRadius: 3 }} />
        </span>
      </span>
    </span>
  );
}
