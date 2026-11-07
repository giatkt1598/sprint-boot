import Drawer from '@mui/material/Drawer';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { History, Maximize2, Plus, Sparkles, X } from 'lucide-react';
import { createContext, use, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { sem, shadow } from '../../theme/tokens';
import { useAssistantStatus } from './api';
import { ChatThread } from './ChatThread';
import { ConversationPicker, QuickPrompts, RecentConversations } from './ConversationPicker';

export interface IssueRef {
  id: string;
  key: string;
}

interface AssistantControls {
  available: boolean;
  open: () => void;
  /** The issue on screen; its conversations come first and its key goes with every question. */
  setViewing: (issue: IssueRef | null) => void;
}

const AssistantContext = createContext<AssistantControls>({ available: false, open: () => undefined, setViewing: () => undefined });

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

function Panel({ issue, onClose }: { issue: IssueRef | null; onClose: () => void }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  // Opening the panel starts a new conversation; an older one is picked from the history.
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [history, setHistory] = useState<HTMLElement | null>(null);

  return (
    <div className="flex h-full flex-col">
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

/** Mounted by the shells, so the panel opens from any page and survives moving between pages. */
export function AssistantHost({ children }: { children: ReactNode }) {
  const status = useAssistantStatus();
  const [state, setState] = useState<{ open: boolean; session: number }>({ open: false, session: 0 });
  const [viewing, setViewing] = useState<IssueRef | null>(null);
  const available = status.data?.available ?? false;

  const controls = useMemo<AssistantControls>(
    () => ({ available, open: () => setState((current) => ({ open: true, session: current.session + 1 })), setViewing }),
    [available],
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
        open={state.open}
        onClose={() => setState((current) => ({ ...current, open: false }))}
        slotProps={{ backdrop: { invisible: true }, paper: { sx: { width: 480, maxWidth: '100%', boxShadow: shadow.drawer } } }}
      >
        {state.open && <Panel key={state.session} issue={viewing} onClose={() => setState((current) => ({ ...current, open: false }))} />}
      </Drawer>
    </AssistantContext>
  );
}

/** The TopBar entry point. */
export function AssistantButton() {
  const { t } = useTranslation();
  const { available, open } = useAssistant();
  if (!available) {
    return null;
  }

  return (
    <Tooltip title={t('assistant.title')}>
      <IconButton size="small" aria-label={t('assistant.open')} onClick={() => open()}>
        <Sparkles size={20} strokeWidth={1.5} aria-hidden />
      </IconButton>
    </Tooltip>
  );
}
