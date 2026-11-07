import Box from '@mui/material/Box';
import ButtonBase from '@mui/material/ButtonBase';
import Drawer from '@mui/material/Drawer';
import Grow from '@mui/material/Grow';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { History, LoaderCircle, Maximize2, Minus, Plus, Sparkles, X } from 'lucide-react';
import { createContext, use, useEffect, useMemo, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { Badge } from '../../components/Badge';
import { font, radius, sem, shadow, size } from '../../theme/tokens';
import { useAssistantStatus, useConversation } from './api';
import { ChatThread } from './ChatThread';
import { ConversationPicker, QuickPrompts, RecentConversations } from './ConversationPicker';

export interface IssueRef {
  id: string;
  key: string;
}

interface AssistantControls {
  available: boolean;
  isOpen: boolean;
  /** Opens the panel on a new conversation; an open panel stays as it is. */
  open: () => void;
  close: () => void;
  /** The issue on screen; its conversations come first and its key goes with every question. */
  setViewing: (issue: IssueRef | null) => void;
}

const AssistantContext = createContext<AssistantControls>({
  available: false,
  isOpen: false,
  open: () => undefined,
  close: () => undefined,
  setViewing: () => undefined,
});

export const useAssistant = () => use(AssistantContext);

/** Tells the assistant which issue is on screen while the calling component is mounted. */
export function useViewingIssue(issue: IssueRef | null) {
  const { setViewing } = useAssistant();
  const id = issue?.id;
  const key = issue?.key;
  useEffect(() => {
    if (!id || !key) {
      return undefined;
    }
    setViewing({ id, key });
    return () => setViewing(null);
  }, [id, key, setViewing]);
}

/** The minimized panel: one line in the bottom right corner, which opens the conversation again. */
function MinimizedCard({ title, issueKey, busy, onRestore, onClose }: {
  title: string;
  issueKey?: string;
  busy: boolean;
  onRestore: () => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  return (
    <Grow in style={{ transformOrigin: 'bottom right' }}>
      <Box
        role="complementary"
        aria-label={t('assistant.title')}
        className="fixed bottom-6 right-6 flex items-center"
        sx={{
          width: 340,
          maxWidth: 'calc(100vw - 48px)',
          zIndex: (theme) => theme.zIndex.drawer,
          backgroundColor: sem('bg-surface'),
          border: `1px solid ${sem('border-default')}`,
          borderRadius: `${radius.card}px`,
          boxShadow: shadow.popover,
        }}
      >
        <ButtonBase
          onClick={onRestore}
          aria-label={t('assistant.restore', { title })}
          className="flex min-w-0 flex-1 items-center gap-3 py-2 pl-2 pr-1 text-left"
          sx={{ borderRadius: `${radius.card}px`, '&:hover': { backgroundColor: sem('bg-hover') } }}
        >
          <span
            aria-hidden
            className="flex shrink-0 items-center justify-center"
            style={{ width: 28, height: 28, borderRadius: radius.full, backgroundColor: sem('bg-selected'), color: sem('text-accent') }}
          >
            {busy ? <LoaderCircle size={16} strokeWidth={1.5} className="animate-spin" /> : <Sparkles size={16} strokeWidth={1.5} />}
          </span>
          <span className="min-w-0 flex-1 truncate" style={{ fontSize: font.size.md, fontWeight: font.weight.medium, color: sem('text-primary') }}>
            {title}
          </span>
          {busy ? (
            <span className="shrink-0" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{t('assistant.answering')}</span>
          ) : (
            issueKey && <Badge tone="subtle">{issueKey}</Badge>
          )}
        </ButtonBase>
        <IconButton size="small" aria-label={t('common.close')} onClick={onClose} sx={{ marginRight: 1 }}>
          <X size={16} strokeWidth={1.5} aria-hidden />
        </IconButton>
      </Box>
    </Grow>
  );
}

function Panel({ issue, minimized, onMinimize, onRestore, onClose }: {
  issue: IssueRef | null;
  minimized: boolean;
  onMinimize: () => void;
  onRestore: () => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  // Opening the panel starts a new conversation; an older one is picked from the history.
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [history, setHistory] = useState<HTMLElement | null>(null);
  const [busy, setBusy] = useState(false);
  const [minimizeTip, setMinimizeTip] = useState(false);
  const detail = useConversation(conversationId);
  const conversation = detail.data?.conversation;
  const title = conversation?.title || t('assistant.untitled');
  const issueKey = conversation ? (conversation.issueKey ?? undefined) : issue?.key;

  return (
    <div className="flex h-full flex-col">
      {/* The panel stays mounted while minimized, so an answer keeps streaming. */}
      {minimized && createPortal(<MinimizedCard title={title} issueKey={issueKey} busy={busy} onRestore={onRestore} onClose={onClose} />, document.body)}
      <div className="flex items-center gap-1 py-2 pl-4 pr-2" style={{ borderBottom: `1px solid ${sem('border-default')}` }}>
        <Sparkles size={16} strokeWidth={1.5} aria-hidden />
        <Typography variant="h3" component="h2" className="min-w-0 flex-1 truncate pl-1">
          {issue ? t('assistant.titleIssue', { key: issue.key }) : t('assistant.title')}
        </Typography>
        <Tooltip title={t('assistant.history')}>
          <IconButton size="small" aria-label={t('assistant.history')} aria-haspopup="dialog" onClick={(event) => setHistory(event.currentTarget)}>
            <History size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
        </Tooltip>
        <Tooltip title={t('assistant.newConversation')}>
          <IconButton size="small" aria-label={t('assistant.newConversation')} onClick={() => setConversationId(null)}>
            <Plus size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
        </Tooltip>
        <Tooltip title={t('assistant.openPage')}>
          <IconButton
            size="small"
            aria-label={t('assistant.openPage')}
            onClick={() => {
              onClose();
              void navigate(conversationId ? `/assistant/${conversationId}` : '/assistant');
            }}
          >
            <Maximize2 size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
        </Tooltip>
        {/*
          The panel slides away under the pointer, so no mouseleave closes this tooltip: left open, it would ride along
          past the right edge and make the page scroll. It closes on click, and is fixed so it can never overflow.
        */}
        <Tooltip
          title={t('assistant.minimize')}
          open={minimizeTip}
          onOpen={() => setMinimizeTip(true)}
          onClose={() => setMinimizeTip(false)}
          slotProps={{ popper: { popperOptions: { strategy: 'fixed' } } }}
        >
          <IconButton
            size="small"
            aria-label={t('assistant.minimize')}
            onClick={() => {
              setMinimizeTip(false);
              onMinimize();
            }}
          >
            <Minus size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
        </Tooltip>
        <IconButton size="small" aria-label={t('common.close')} onClick={onClose}>
          <X size={16} strokeWidth={1.5} aria-hidden />
        </IconButton>
      </div>
      <ConversationPicker
        anchor={history}
        issueId={issue?.id}
        selectedId={conversationId}
        onClose={() => setHistory(null)}
        onPick={(id) => {
          setHistory(null);
          setConversationId(id);
        }}
      />
      <ChatThread
        key={conversationId ?? 'new'}
        conversationId={conversationId}
        issueId={issue?.id}
        onConversation={setConversationId}
        onBusyChange={setBusy}
        active={!minimized}
        intro={(ask) => (
          <div className="flex flex-col gap-4">
            <RecentConversations issueId={issue?.id} onPick={setConversationId} />
            {issue && <QuickPrompts onAsk={ask} />}
          </div>
        )}
      />
    </div>
  );
}

/**
 * Mounted above every signed-in page, so the panel survives moving between them. It is not modal: the page beside it
 * stays usable, and only the panel's own column is covered.
 */
export function AssistantHost({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const status = useAssistantStatus();
  const [state, setState] = useState<{ open: boolean; minimized: boolean; session: number }>({ open: false, minimized: false, session: 0 });
  const [viewing, setViewing] = useState<IssueRef | null>(null);
  const available = status.data?.available ?? false;

  const controls = useMemo<AssistantControls>(
    () => ({
      available,
      isOpen: state.open && !state.minimized,
      // A minimized panel comes back as it was.
      open: () => setState((current) => (current.open ? { ...current, minimized: false } : { open: true, minimized: false, session: current.session + 1 })),
      close: () => setState((current) => ({ ...current, open: false, minimized: false })),
      setViewing,
    }),
    [available, state.open, state.minimized],
  );

  useEffect(() => {
    if (!available) {
      setState((current) => ({ ...current, open: false }));
    }
  }, [available]);

  return (
    <AssistantContext value={controls}>
      {children}
      <Drawer
        anchor="right"
        variant="persistent"
        open={state.open && !state.minimized}
        slotProps={{
          paper: {
            'aria-label': t('assistant.title'),
            role: 'complementary',
            // Below the top bar, which keeps its Assistant button reachable.
            sx: { top: size.topBar, height: `calc(100% - ${size.topBar}px)`, width: 480, maxWidth: '100%', boxShadow: shadow.drawer },
          },
        }}
      >
        {state.open && (
          <Panel
            key={state.session}
            issue={viewing}
            minimized={state.minimized}
            onMinimize={() => setState((current) => ({ ...current, minimized: true }))}
            onRestore={controls.open}
            onClose={controls.close}
          />
        )}
      </Drawer>
    </AssistantContext>
  );
}

/** The TopBar entry point. */
export function AssistantButton() {
  const { t } = useTranslation();
  const { available, isOpen, open, close } = useAssistant();
  if (!available) {
    return null;
  }

  return (
    <Tooltip title={t('assistant.title')}>
      <IconButton size="small" aria-label={t('assistant.open')} aria-pressed={isOpen} onClick={() => (isOpen ? close() : open())}>
        <Sparkles size={20} strokeWidth={1.5} aria-hidden />
      </IconButton>
    </Tooltip>
  );
}
