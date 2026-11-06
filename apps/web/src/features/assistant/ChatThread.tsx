import Typography from '@mui/material/Typography';
import { useQueryClient } from '@tanstack/react-query';
import { Bot, Check, LoaderCircle, Square, Wrench, X } from 'lucide-react';
import { useEffect, useRef, useState, type KeyboardEvent, type MouseEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate } from 'react-router';
import { Badge, type BadgeTone } from '../../components/Badge';
import { Button } from '../../components/Button';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { TextField } from '../../components/TextField';
import { font, radius, sem } from '../../theme/tokens';
import { RichTextView } from '../issues/RichTextEditor';
import {
  ActionState,
  assistantKeys,
  createConversation,
  streamAssistant,
  useConversation,
  type AssistantAction,
  type AssistantEvent,
  type AssistantMessage,
} from './api';

interface Live {
  question: string | null;
  text: string;
  tools: { name: string; state: string }[];
  messages: AssistantMessage[];
}

const EMPTY_LIVE: Live = { question: null, text: '', tools: [], messages: [] };

const actionTones: Record<number, BadgeTone> = {
  [ActionState.proposed]: 'warning',
  [ActionState.confirmed]: 'success',
  [ActionState.declined]: 'subtle',
  [ActionState.failed]: 'danger',
};

const actionLabels: Record<number, string> = {
  [ActionState.proposed]: 'proposed',
  [ActionState.confirmed]: 'confirmed',
  [ActionState.declined]: 'declined',
  [ActionState.failed]: 'failed',
};

/** Pretty JSON when the model sent valid JSON, the raw text otherwise. */
const pretty = (json: string) => {
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
};

function ActionCard({ action, busy, onDecide }: { action: AssistantAction; busy: boolean; onDecide: (action: AssistantAction, confirm: boolean) => void }) {
  const { t } = useTranslation();
  const state = action.state ?? ActionState.proposed;

  return (
    <div
      className="flex flex-col gap-2 p-3"
      style={{ border: `1px solid ${sem(state === ActionState.proposed ? 'border-focus' : 'border-default')}`, borderRadius: radius.card }}
    >
      <div className="flex items-center gap-2">
        <Wrench size={16} strokeWidth={1.5} aria-hidden />
        <Typography variant="body2" className="min-w-0 flex-1" sx={{ fontWeight: font.weight.medium }}>
          {t('assistant.actionTitle', { tool: action.tool })}
        </Typography>
        <Badge tone={actionTones[state]}>{t(`assistant.actionState.${actionLabels[state]}`)}</Badge>
      </div>
      <pre className="m-0 overflow-x-auto p-2" style={{ fontSize: font.size.xs, backgroundColor: sem('bg-subtle'), borderRadius: radius.control }}>
        {pretty(action.arguments ?? '{}')}
      </pre>
      {state === ActionState.failed && action.result && (
        <Typography variant="body2" sx={{ color: sem('feedback-danger-text') }}>{action.result}</Typography>
      )}
      {state === ActionState.proposed && (
        <div className="flex justify-end gap-2">
          <Button kind="ghost" disabled={busy} startIcon={<X size={16} strokeWidth={1.5} aria-hidden />} onClick={() => onDecide(action, false)}>
            {t('assistant.decline')}
          </Button>
          <Button disabled={busy} startIcon={<Check size={16} strokeWidth={1.5} aria-hidden />} onClick={() => onDecide(action, true)}>
            {t('assistant.confirm')}
          </Button>
        </div>
      )}
    </div>
  );
}

function AssistantBubble({ message, busy, onDecide }: { message: AssistantMessage; busy: boolean; onDecide: (action: AssistantAction, confirm: boolean) => void }) {
  const { t } = useTranslation();
  const navigate = useNavigate();

  // Issue links in the answer open inside the app instead of reloading it.
  const followLink = (event: MouseEvent<HTMLDivElement>) => {
    const link = (event.target as HTMLElement).closest('a');
    const href = link?.getAttribute('href');
    if (href?.startsWith('/')) {
      event.preventDefault();
      void navigate(href);
    }
  };

  return (
    <div className="flex flex-col gap-2">
      {(message.tools ?? []).length > 0 && (
        <div className="flex flex-wrap gap-1">
          {[...new Set(message.tools)].map((tool) => <Badge key={tool} tone="subtle">{t('assistant.usedTool', { tool })}</Badge>)}
        </div>
      )}
      {message.html && (
        <div onClick={followLink}>
          <RichTextView html={message.html} />
        </div>
      )}
      {message.stopReason && (
        <Typography variant="body2" color="textSecondary">
          {t(message.stopReason === 'refusal' ? 'assistant.refused' : message.stopReason === 'max_tokens' ? 'assistant.cutOff' : 'assistant.stopped')}
        </Typography>
      )}
      {(message.actions ?? []).map((action) => <ActionCard key={action.id} action={action} busy={busy} onDecide={onDecide} />)}
      {(message.sources ?? []).length > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          <Typography variant="body2" color="textSecondary">{t('assistant.sources')}</Typography>
          {message.sources!.map((key) => (
            <RouterLink key={key} to={`/issues/${key}`} style={{ fontSize: font.size.xs, color: sem('text-accent') }}>{key}</RouterLink>
          ))}
        </div>
      )}
    </div>
  );
}

function UserBubble({ text }: { text: string }) {
  return (
    <div className="flex justify-end">
      <div
        className="px-3 py-2"
        style={{ maxWidth: '85%', whiteSpace: 'pre-wrap', backgroundColor: sem('bg-subtle'), borderRadius: radius.card, fontSize: font.size.md }}
      >
        {text}
      </div>
    </div>
  );
}

/**
 * One conversation: what was said, the answer as it streams, and the changes waiting for confirmation. With no conversation
 * yet, the first question starts one, tied to the issue when there is one.
 */
export function ChatThread({ conversationId, issueId, onConversation }: {
  conversationId: string | null;
  issueId?: string;
  onConversation: (id: string) => void;
}) {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const detail = useConversation(conversationId);
  const [draft, setDraft] = useState('');
  const [live, setLive] = useState<Live>(EMPTY_LIVE);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const abort = useRef<AbortController | null>(null);
  const bottom = useRef<HTMLDivElement>(null);

  useEffect(() => () => abort.current?.abort(), []);
  useEffect(() => {
    bottom.current?.scrollIntoView({ block: 'end' });
  }, [detail.data, live]);

  const describe = (event: AssistantEvent) => {
    const key = `errors.${event.code?.split(':')[1]}`;
    return event.code && i18n.exists(key) ? t(key) : event.text || t('errors.generic');
  };

  const run = async (id: string, path: string, body: unknown, question: string | null) => {
    abort.current = new AbortController();
    setBusy(true);
    setError(null);
    setLive({ ...EMPTY_LIVE, question });
    try {
      await streamAssistant(path, body, (event) => {
        setLive((current) => {
          switch (event.type) {
            case 'delta':
              return { ...current, text: current.text + (event.text ?? '') };
            case 'tool':
              return {
                ...current,
                tools: event.state === 'running'
                  ? [...current.tools, { name: event.tool ?? '', state: 'running' }]
                  : current.tools.map((tool, index, all) => (index === all.length - 1 ? { ...tool, state: event.state ?? 'done' } : tool)),
              };
            case 'message':
              // The finished text now shows inside the message; what streams next belongs to the following step.
              return { ...current, text: '', messages: [...current.messages, event.message!] };
            default:
              return current;
          }
        });
        if (event.type === 'error') {
          setError(describe(event));
        }
      }, abort.current.signal);
    } catch (failure) {
      if (!(failure instanceof DOMException && failure.name === 'AbortError')) {
        setError(t('errors.generic'));
      }
    } finally {
      setBusy(false);
      await queryClient.invalidateQueries({ queryKey: assistantKeys.detail(id) });
      await queryClient.invalidateQueries({ queryKey: ['assistant', 'conversations'] });
      setLive(EMPTY_LIVE);
    }
  };

  const send = async () => {
    const text = draft.trim();
    if (!text || busy) {
      return;
    }
    setDraft('');
    let id = conversationId;
    if (!id) {
      try {
        id = (await createConversation(issueId)).id!;
      } catch {
        setError(t('errors.generic'));
        setDraft(text);
        return;
      }
      onConversation(id);
    }
    await run(id, `/api/v1/assistant/conversations/${id}/messages`, { text }, text);
  };

  const decide = (action: AssistantAction, confirm: boolean) =>
    conversationId && void run(conversationId, `/api/v1/assistant/conversations/${conversationId}/actions/${action.id}/${confirm ? 'confirm' : 'decline'}`, undefined, null);

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void send();
    }
  };

  const messages = detail.data?.messages ?? [];

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto p-4" aria-live="polite">
        {conversationId && detail.isPending && <LoadingState />}
        {detail.isError && <ErrorState onRetry={() => void detail.refetch()} />}
        {!conversationId && !live.question && (
          <div className="flex flex-col items-center gap-2 py-8 text-center">
            <Bot size={24} strokeWidth={1.5} aria-hidden />
            <Typography variant="body2" color="textSecondary">{t(issueId ? 'assistant.emptyIssue' : 'assistant.empty')}</Typography>
          </div>
        )}
        {messages.map((message) =>
          message.role === 'user'
            ? <UserBubble key={message.id} text={message.text ?? ''} />
            : <AssistantBubble key={message.id} message={message} busy={busy} onDecide={decide} />,
        )}
        {live.question && <UserBubble text={live.question} />}
        {live.messages.map((message) => <AssistantBubble key={message.id} message={message} busy={busy} onDecide={decide} />)}
        {live.tools.length > 0 && busy && (
          <div className="flex flex-wrap gap-1">
            {live.tools.map((tool, index) => (
              <Badge key={index} tone={tool.state === 'failed' ? 'danger' : 'subtle'}>
                {t(tool.state === 'running' ? 'assistant.runningTool' : 'assistant.usedTool', { tool: tool.name })}
              </Badge>
            ))}
          </div>
        )}
        {live.text && <div style={{ whiteSpace: 'pre-wrap', fontSize: font.size.md }}>{live.text}</div>}
        {busy && !live.text && (
          <div className="flex items-center gap-2" role="status">
            <LoaderCircle size={16} strokeWidth={1.5} className="animate-spin" aria-hidden />
            <Typography variant="body2" color="textSecondary">{t('assistant.thinking')}</Typography>
          </div>
        )}
        {error && (
          <Typography role="alert" variant="body2" sx={{ color: sem('feedback-danger-text') }}>{error}</Typography>
        )}
        <div ref={bottom} />
      </div>
      <div className="flex items-end gap-2 p-3" style={{ borderTop: `1px solid ${sem('border-default')}` }}>
        <div className="min-w-0 flex-1">
          <TextField
            label={t('assistant.question')}
            placeholder={t('assistant.placeholder')}
            multiline
            maxRows={6}
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={onKeyDown}
          />
        </div>
        {busy ? (
          <Button kind="secondary" startIcon={<Square size={16} strokeWidth={1.5} aria-hidden />} onClick={() => abort.current?.abort()}>
            {t('assistant.stop')}
          </Button>
        ) : null}
      </div>
    </div>
  );
}
