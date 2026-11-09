import IconButton from '@mui/material/IconButton';
import MenuList from '@mui/material/MenuList';
import Popper from '@mui/material/Popper';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { CloseIcon, SearchIcon } from '../../components/icons';
import { TicketTypeIcon, type TicketType as IconType } from '../../components/TicketIcons';
import { MenuItem } from '../../components/Menu';
import { SearchInput } from '../../components/SearchInput';
import { radius, sem, shadow, size } from '../../theme/tokens';

const DEBOUNCE_MS = 200;
const QUICK_RESULTS = 6;

/** Ticket search in the top bar. `compact` shows only a button, which opens the field across the whole bar. */
export function QuickSearch({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);
  const navigate = useNavigate();
  const anchor = useRef<HTMLDivElement>(null);
  const input = useRef<HTMLInputElement>(null);
  const [text, setText] = useState('');
  const [debounced, setDebounced] = useState('');
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(text.trim()), DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [text]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        setExpanded(true);
        input.current?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const results = useQuery({
    queryKey: ['tickets', 'quick', debounced],
    enabled: debounced.length > 0,
    queryFn: () => unwrap(apiClient.GET('/api/v1/ticket/search', { params: { query: { Text: debounced, MaxResultCount: QUICK_RESULTS } } })),
  });

  const collapse = () => {
    setOpen(false);
    setExpanded(false);
  };

  const go = (path: string) => {
    collapse();
    setText('');
    void navigate(path);
  };

  const seeAll = () => go(`/search?text=${encodeURIComponent(text)}`);

  if (compact && !expanded) {
    return (
      <IconButton size="small" aria-label={t('topBar.search')} onClick={() => setExpanded(true)}>
        <SearchIcon size={20} strokeWidth={1.5} aria-hidden />
      </IconButton>
    );
  }

  return (
    <div
      ref={anchor}
      className={compact ? 'fixed inset-x-0 top-0 flex items-center gap-2 px-3' : undefined}
      style={compact ? { height: size.topBar, zIndex: 1250, backgroundColor: 'var(--sb-chrome-bg)', borderBottom: `1px solid ${sem('border-default')}` } : undefined}
    >
      <SearchInput
        label={t('topBar.search')}
        inputRef={input}
        width={compact ? '100%' : undefined}
        autoFocus={compact}
        value={text}
        onChange={(event) => { setText(event.target.value); setOpen(true); }}
        onBlur={() => window.setTimeout(() => (compact && !text.trim() ? collapse() : setOpen(false)), 150)}
        onKeyDown={(event) => {
          if (event.key === 'Enter' && text.trim()) {
            if (results.data?.exactKey) {
              go(`/browse/${results.data.exactKey}`);
            } else {
              seeAll();
            }
          }
          if (event.key === 'Escape') {
            collapse();
          }
        }}
      />
      {compact && (
        <IconButton size="small" aria-label={t('common.close')} onClick={collapse}>
          <CloseIcon size={20} strokeWidth={1.5} aria-hidden />
        </IconButton>
      )}
      <Popper open={open && debounced.length > 0} anchorEl={anchor.current} placement="bottom-end" style={{ zIndex: 1300 }}>
        <MenuList
          className="mt-1 flex flex-col p-2"
          style={{ width: 400, maxWidth: 'calc(100vw - 24px)', backgroundColor: sem('bg-surface'), border: `1px solid ${sem('border-default')}`, borderRadius: radius.card, boxShadow: shadow.popover }}
        >
          {(results.data?.items ?? []).map((ticket) => (
            <MenuItem key={ticket.id} onClick={() => go(`/browse/${ticket.key}`)}>
              <TicketTypeIcon type={ticket.type?.icon as IconType} />
              <span className="shrink-0">{ticket.key}</span>
              <span className="truncate">{ticket.title}</span>
            </MenuItem>
          ))}
          {results.data?.totalCount === 0 && <MenuItem disabled>{t('tickets.noResultsHint')}</MenuItem>}
          <MenuItem onClick={seeAll}>{t('tickets.seeAll')}</MenuItem>
        </MenuList>
      </Popper>
    </div>
  );
}
