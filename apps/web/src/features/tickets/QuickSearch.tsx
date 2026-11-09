import MenuList from '@mui/material/MenuList';
import Popper from '@mui/material/Popper';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { TicketTypeIcon, type TicketType as IconType } from '../../components/TicketIcons';
import { MenuItem } from '../../components/Menu';
import { SearchInput } from '../../components/SearchInput';
import { radius, sem, shadow } from '../../theme/tokens';

const DEBOUNCE_MS = 200;
const QUICK_RESULTS = 6;

export function QuickSearch() {
  const { t } = useTranslation();
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

  const go = (path: string) => {
    setOpen(false);
    setText('');
    void navigate(path);
  };

  const seeAll = () => go(`/search?text=${encodeURIComponent(text)}`);

  return (
    <div ref={anchor}>
      <SearchInput
        label={t('topBar.search')}
        inputRef={input}
        value={text}
        onChange={(event) => { setText(event.target.value); setOpen(true); }}
        onBlur={() => window.setTimeout(() => setOpen(false), 150)}
        onKeyDown={(event) => {
          if (event.key === 'Enter' && text.trim()) {
            if (results.data?.exactKey) {
              go(`/tickets/${results.data.exactKey}`);
            } else {
              seeAll();
            }
          }
          if (event.key === 'Escape') {
            setOpen(false);
          }
        }}
      />
      <Popper open={open && debounced.length > 0} anchorEl={anchor.current} placement="bottom-end" style={{ zIndex: 1300 }}>
        <MenuList
          className="mt-1 flex flex-col p-2"
          style={{ width: 400, backgroundColor: sem('bg-surface'), border: `1px solid ${sem('border-default')}`, borderRadius: radius.card, boxShadow: shadow.popover }}
        >
          {(results.data?.items ?? []).map((ticket) => (
            <MenuItem key={ticket.id} onClick={() => go(`/tickets/${ticket.key}`)}>
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
