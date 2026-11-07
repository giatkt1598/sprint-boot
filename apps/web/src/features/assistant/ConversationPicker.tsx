import ButtonBase from '@mui/material/ButtonBase';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Badge } from '../../components/Badge';
import { Popover } from '../../components/Popover';
import { SearchInput } from '../../components/SearchInput';
import { LoadingState } from '../../components/StateViews';
import { formatDateTime } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';
import { useConversations, type Conversation } from './api';

const RECENT = 3;

/** Newest first, with the conversations about the issue on screen ahead of the rest. */
export function useRankedConversations(issueId: string | undefined) {
  const list = useConversations();
  const all = list.data ?? [];
  const ranked = issueId ? [...all.filter((item) => item.issueId === issueId), ...all.filter((item) => item.issueId !== issueId)] : all;
  return { ...list, ranked };
}

function ConversationRow({ item, selected, onPick }: { item: Conversation; selected?: boolean; onPick: (id: string) => void }) {
  const { t, i18n } = useTranslation();
  return (
    <ButtonBase
      onClick={() => onPick(item.id!)}
      aria-current={selected ? 'true' : undefined}
      className="flex w-full min-w-0 flex-col items-start px-2 py-1 text-left"
      sx={{
        borderRadius: `${radius.control}px`,
        backgroundColor: selected ? sem('bg-selected') : undefined,
        '&:hover': { backgroundColor: sem('bg-hover') },
      }}
    >
      <span className="w-full truncate" style={{ fontSize: font.size.md, color: sem('text-primary') }}>{item.title || t('assistant.untitled')}</span>
      <span className="flex items-center gap-1" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>
        {item.issueKey && <Badge tone="subtle">{item.issueKey}</Badge>}
        {formatDateTime(item.updatedAt, i18n.language)}
      </span>
    </ButtonBase>
  );
}

const matches = (item: Conversation, text: string) =>
  !text || (item.title ?? '').toLowerCase().includes(text) || (item.issueKey ?? '').toLowerCase().includes(text);

/** Every earlier conversation, searchable; those about the issue on screen come first, under their own heading. */
export function ConversationPicker({ anchor, issueId, selectedId, onClose, onPick }: {
  anchor: HTMLElement | null;
  issueId?: string;
  selectedId?: string | null;
  onClose: () => void;
  onPick: (id: string) => void;
}) {
  const { t } = useTranslation();
  const conversations = useRankedConversations(issueId);
  const [search, setSearch] = useState('');
  const text = search.trim().toLowerCase();
  const found = conversations.ranked.filter((item) => matches(item, text));
  const groups = issueId
    ? [
        { key: 'issue', label: t('assistant.thisIssue'), items: found.filter((item) => item.issueId === issueId) },
        { key: 'other', label: t('assistant.otherConversations'), items: found.filter((item) => item.issueId !== issueId) },
      ]
    : [{ key: 'all', label: null, items: found }];

  return (
    <Popover
      open={anchor !== null}
      anchorEl={anchor}
      width={360}
      onClose={() => {
        setSearch('');
        onClose();
      }}
    >
      <div className="flex flex-col gap-2 p-2" role="dialog" aria-label={t('assistant.history')}>
        <SearchInput label={t('assistant.searchConversations')} width={344} autoFocus value={search} onChange={(event) => setSearch(event.target.value)} />
        <div className="flex max-h-96 flex-col gap-2 overflow-y-auto">
          {conversations.isPending && <LoadingState />}
          {conversations.data && found.length === 0 && (
            <Typography variant="body2" color="textSecondary" className="px-2 py-1">
              {t(text ? 'assistant.noMatches' : 'assistant.noConversations')}
            </Typography>
          )}
          {groups.filter((group) => group.items.length > 0).map((group) => (
            <div key={group.key} className="flex flex-col">
              {group.label && (
                <Typography variant="body2" color="textSecondary" className="px-2 py-1" sx={{ fontSize: font.size.xs, fontWeight: font.weight.medium }}>
                  {group.label}
                </Typography>
              )}
              {group.items.map((item) => <ConversationRow key={item.id} item={item} selected={item.id === selectedId} onPick={onPick} />)}
            </div>
          ))}
        </div>
      </div>
    </Popover>
  );
}

/** The few latest conversations, at the top of a new chat; the rest are one click away. */
export function RecentConversations({ issueId, onPick }: { issueId?: string; onPick: (id: string) => void }) {
  const { t } = useTranslation();
  const conversations = useRankedConversations(issueId);
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const recent = conversations.ranked.slice(0, RECENT);
  const more = conversations.ranked.length - recent.length;
  if (recent.length === 0) {
    return null;
  }

  return (
    <section aria-labelledby="assistant-recent" className="flex flex-col gap-1">
      <div className="flex items-center gap-2 px-2">
        <Typography id="assistant-recent" variant="body2" color="textSecondary" className="flex-1" sx={{ fontSize: font.size.xs, fontWeight: font.weight.medium }}>
          {t('assistant.recent')}
        </Typography>
        {more > 0 && (
          <ButtonBase
            aria-haspopup="dialog"
            onClick={(event) => setAnchor(event.currentTarget)}
            sx={{ fontSize: font.size.xs, color: sem('text-accent'), '&:hover': { textDecoration: 'underline' } }}
          >
            {t('assistant.viewAll', { count: more })}
          </ButtonBase>
        )}
      </div>
      {recent.map((item) => <ConversationRow key={item.id} item={item} onPick={onPick} />)}
      <ConversationPicker
        anchor={anchor}
        issueId={issueId}
        onClose={() => setAnchor(null)}
        onPick={(id) => {
          setAnchor(null);
          onPick(id);
        }}
      />
    </section>
  );
}

const PROMPTS = ['summarize', 'explain', 'nextSteps'] as const;

/** One-click questions about the issue on screen; the assistant gets the issue with them. */
export function QuickPrompts({ onAsk }: { onAsk: (text: string) => void }) {
  const { t } = useTranslation();
  return (
    <section aria-labelledby="assistant-prompts" className="flex flex-col gap-1">
      <Typography id="assistant-prompts" variant="body2" color="textSecondary" className="px-2" sx={{ fontSize: font.size.xs, fontWeight: font.weight.medium }}>
        {t('assistant.suggestions')}
      </Typography>
      <div className="flex flex-wrap gap-2 px-2">
        {PROMPTS.map((prompt) => (
          <ButtonBase
            key={prompt}
            onClick={() => onAsk(t(`assistant.prompts.${prompt}`))}
            className="px-3 py-1"
            sx={{
              fontSize: font.size.md,
              color: sem('text-primary'),
              border: `1px solid ${sem('border-default')}`,
              borderRadius: `${radius.full}px`,
              '&:hover': { backgroundColor: sem('bg-hover') },
            }}
          >
            {t(`assistant.prompts.${prompt}`)}
          </ButtonBase>
        ))}
      </div>
    </section>
  );
}
