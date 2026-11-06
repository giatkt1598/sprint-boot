import Drawer from '@mui/material/Drawer';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { Maximize2, Plus, Sparkles, X } from 'lucide-react';
import { createContext, use, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router';
import { sem, shadow } from '../../theme/tokens';
import { useAssistantStatus, useConversations } from './api';
import { ChatThread } from './ChatThread';

interface IssueRef {
  id: string;
  key: string;
}

interface AssistantControls {
  available: boolean;
  /** Opens the panel; with an issue, on that issue's latest conversation. */
  open: (issue?: IssueRef) => void;
}

const AssistantContext = createContext<AssistantControls>({ available: false, open: () => undefined });

export const useAssistant = () => use(AssistantContext);

function Panel({ issue, onClose }: { issue: IssueRef | null; onClose: () => void }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const issueConversations = useConversations(issue?.id);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [fresh, setFresh] = useState(false);

  // An issue chat picks up where the person left off.
  const latest = issue ? issueConversations.data?.[0]?.id ?? null : null;
  const current = conversationId ?? (fresh ? null : latest);

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center gap-2 px-4 py-3" style={{ borderBottom: `1px solid ${sem('border-default')}` }}>
        <Sparkles size={16} strokeWidth={1.5} aria-hidden />
        <Typography variant="h3" component="h2" className="min-w-0 flex-1 truncate">
          {issue ? t('assistant.titleIssue', { key: issue.key }) : t('assistant.title')}
        </Typography>
        <Tooltip title={t('assistant.newConversation')}>
          <IconButton size="small" aria-label={t('assistant.newConversation')} onClick={() => { setConversationId(null); setFresh(true); }}>
            <Plus size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
        </Tooltip>
        <Tooltip title={t('assistant.openPage')}>
          <IconButton
            size="small"
            aria-label={t('assistant.openPage')}
            onClick={() => {
              onClose();
              void navigate(current ? `/assistant/${current}` : '/assistant');
            }}
          >
            <Maximize2 size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
        </Tooltip>
        <IconButton size="small" aria-label={t('common.close')} onClick={onClose}>
          <X size={16} strokeWidth={1.5} aria-hidden />
        </IconButton>
      </div>
      <ChatThread key={current ?? 'new'} conversationId={current} issueId={issue?.id} onConversation={setConversationId} />
    </div>
  );
}

/** Mounted by the shells, so the panel opens from any page and survives moving between pages. */
export function AssistantHost({ children }: { children: ReactNode }) {
  const status = useAssistantStatus();
  const [state, setState] = useState<{ open: boolean; issue: IssueRef | null; session: number }>({ open: false, issue: null, session: 0 });
  const available = status.data?.available ?? false;

  const controls = useMemo<AssistantControls>(
    () => ({ available, open: (issue) => setState((current) => ({ open: true, issue: issue ?? null, session: current.session + 1 })) }),
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
        {state.open && <Panel key={state.session} issue={state.issue} onClose={() => setState((current) => ({ ...current, open: false }))} />}
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
