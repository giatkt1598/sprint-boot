import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import InputBase from '@mui/material/InputBase';
import Typography from '@mui/material/Typography';
import { useQueryClient } from '@tanstack/react-query';
import { ArrowUp, Bot, Check, LoaderCircle, Square, Wrench, X } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate } from 'react-router';
import { Badge, type BadgeTone } from '../../components/Badge';
import { Button } from '../../components/Button';
import { ErrorState, LoadingState } from '../../components/StateViews';
import { font, radius, sem } from '../../theme/tokens';
import { RichTextView } from '../issues/RichTextEditor';
import { renderStreamingMarkdown, REVEAL_MS, useSmoothText, useStickToBottom } from './streaming';
import {
  ActionState,
  assistantKeys,
  conversationQuery,
  createConversation,
  streamAssistant,
  useConversation,
  type AssistantAction,
  type AssistantEvent,
  type AssistantMessage,
} from './api';

/** One step of the model: the text as it streams, then the saved message once the step ends. */
interface Segment {
  text: string;
  message?: AssistantMessage;
}

interface Live {
  question: string | null;
  tools: { name: string; state: string }[];
  segments: Segment[];
}

const EMPTY_LIVE: Live = { question: null, tools: [], segments: [] };

/** What the stream ended with, kept until the last words have shown. */
interface Ending {
  id: string;
  done?: () => Promise<void>;
  /** A confirmed change ran, so what the pages show may be out of date. */
  changed: boolean;
}

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
  const followLink = useFollowLink();

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

/** Issue links in an answer open inside the app instead of reloading it. */
function useFollowLink() {
  const navigate = useNavigate();
  return (event: MouseEvent<HTMLDivElement>) => {
    const link = (event.target as HTMLElement).closest('a');
    const href = link?.getAttribute('href');
    if (href?.startsWith('/')) {
      event.preventDefault();
      void navigate(href);
    }
  };
}

/**
 * The step being written: Markdown rendered as it arrives, revealed a few words per frame. Once the step is saved and
 * every word has shown, the server's rendering of it takes over.
 */
function StreamingAnswer({ segment, finished, skip, busy, onDecide, onCaughtUp }: {
  segment: Segment;
  finished: boolean;
  skip: boolean;
  busy: boolean;
  onDecide: (action: AssistantAction, confirm: boolean) => void;
  onCaughtUp: (caughtUp: boolean) => void;
}) {
  const followLink = useFollowLink();
  const { shown, marks, caughtUp } = useSmoothText(segment.text, finished || Boolean(segment.message), skip);
  const html = useMemo(() => renderStreamingMarkdown(shown, marks), [shown, marks]);
  // Caught up only once the last words have faded in, so the saved message never cuts a fade short.
  const fading = marks.length > 0;
  const [settled, setSettled] = useState(false);
  useEffect(() => {
    if (!caughtUp) {
      setSettled(false);
      return undefined;
    }
    if (!fading) {
      setSettled(true);
      return undefined;
    }
    const timer = setTimeout(() => setSettled(true), REVEAL_MS);
    return () => clearTimeout(timer);
  }, [caughtUp, fading]);
  useEffect(() => onCaughtUp(settled), [settled, onCaughtUp]);

  if (segment.message && settled) {
    return <AssistantBubble message={segment.message} busy={busy} onDecide={onDecide} />;
  }
  return html ? (
    <div onClick={followLink}>
      <RichTextView html={html} />
    </div>
  ) : null;
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
export function ChatThread({ conversationId, issueId, onConversation, intro, onBusyChange, active = true }: {
  conversationId: string | null;
  /** The issue on screen: a new conversation is tied to it, and it goes with every question. */
  issueId?: string;
  onConversation: (id: string) => void;
  /** Shown above an empty new chat; `ask` sends a question as if typed. */
  intro?: (ask: (text: string) => void) => ReactNode;
  /** Told when an answer starts and ends, for a minimized panel to show it. */
  onBusyChange?: (busy: boolean) => void;
  /** False while hidden, such as in a minimized panel; becoming true puts the cursor in the question box. */
  active?: boolean;
}) {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const detail = useConversation(conversationId);
  const [draft, setDraft] = useState('');
  const [live, setLive] = useState<Live>(EMPTY_LIVE);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ending, setEnding] = useState<Ending | null>(null);
  const [revealed, setRevealed] = useState(true);
  const [skip, setSkip] = useState(false);
  /** How many saved messages there were when the live answer started; later ones show only once it is saved. */
  const [baseline, setBaseline] = useState<number | null>(null);
  const abort = useRef<AbortController | null>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  const scroller = useStickToBottom<HTMLDivElement, HTMLDivElement>();

  useEffect(() => () => abort.current?.abort(), []);
  useEffect(() => onBusyChange?.(busy), [busy, onBusyChange]);
  useEffect(() => {
    if (!active) {
      return undefined;
    }
    // Once the drawer is visible again, so the question can be typed straight away.
    const frame = requestAnimationFrame(() => input.current?.focus());
    return () => cancelAnimationFrame(frame);
  }, [active]);
  // An opened conversation starts at its latest message.
  const loaded = detail.isSuccess;
  const { stick } = scroller;
  useEffect(() => {
    if (loaded) {
      stick();
    }
  }, [loaded, stick]);

  // Once the stream has ended and its last words have shown, the saved conversation replaces the live copy.
  useEffect(() => {
    if (!ending || !revealed) {
      return;
    }
    setEnding(null);
    void (async () => {
      await queryClient.invalidateQueries({ queryKey: assistantKeys.detail(ending.id) });
      await queryClient.invalidateQueries({ queryKey: ['assistant', 'conversations'] });
      if (ending.changed) {
        // The assistant may have changed any issue, sprint or board; the page behind the chat reloads what it shows.
        await queryClient.invalidateQueries({ predicate: (query) => query.queryKey[0] !== 'assistant' });
      }
      await ending.done?.();
      setLive(EMPTY_LIVE);
      setBaseline(null);
      setSkip(false);
      setBusy(false);
    })();
  }, [ending, revealed, queryClient]);

  const describe = (event: AssistantEvent) => {
    const key = `errors.${event.code?.split(':')[1]}`;
    return event.code && i18n.exists(key) ? t(key) : event.text || t('errors.generic');
  };

  /** Streams one answer; `done` runs once it is saved and shown, before the live copy is cleared. */
  const run = async (id: string, path: string, body: unknown, question: string | null, done?: () => Promise<void>) => {
    abort.current = new AbortController();
    setBusy(true);
    setError(null);
    setLive({ ...EMPTY_LIVE, question });
    setBaseline(detail.data?.messages?.length ?? 0);
    setRevealed(true);
    scroller.stick();
    let changed = false;
    try {
      await streamAssistant(path, body, (event) => {
        setLive((current) => {
          const last = current.segments[current.segments.length - 1];
          switch (event.type) {
            case 'delta':
              return {
                ...current,
                segments: last && !last.message
                  ? [...current.segments.slice(0, -1), { text: last.text + (event.text ?? '') }]
                  : [...current.segments, { text: event.text ?? '' }],
              };
            case 'tool':
              return {
                ...current,
                tools: event.state === 'running'
                  ? [...current.tools, { name: event.tool ?? '', state: 'running' }]
                  : current.tools.map((tool, index, all) => (index === all.length - 1 ? { ...tool, state: event.state ?? 'done' } : tool)),
              };
            case 'message':
              // The step is saved; what streams next belongs to the following step.
              return {
                ...current,
                segments: last && !last.message
                  ? [...current.segments.slice(0, -1), { ...last, message: event.message! }]
                  : [...current.segments, { text: '', message: event.message! }],
              };
            default:
              return current;
          }
        });
        if (event.type === 'error') {
          setError(describe(event));
        }
        if (event.type === 'action' && event.action?.state === ActionState.confirmed) {
          changed = true;
        }
      }, abort.current.signal);
    } catch (failure) {
      if (!(failure instanceof DOMException && failure.name === 'AbortError')) {
        setError(t('errors.generic'));
      }
    } finally {
      // Still busy until the last words have shown; see the ending effect.
      setEnding({ id, done, changed });
    }
  };

  const send = async (typed: string) => {
    const text = typed.trim();
    if (!text || busy) {
      return;
    }
    setDraft('');
    const body = { text, issueId: issueId ?? null };
    if (conversationId) {
      await run(conversationId, `/api/v1/assistant/conversations/${conversationId}/messages`, body, text);
      return;
    }

    let id: string;
    try {
      id = (await createConversation(issueId)).id!;
    } catch {
      setError(t('errors.generic'));
      setDraft(typed);
      return;
    }
    // Parents key the thread by conversation, so telling them now would remount it and cut off the first answer.
    // They hear of it once the answer is saved, with the conversation already loaded so nothing flickers.
    await run(id, `/api/v1/assistant/conversations/${id}/messages`, body, text, async () => {
      await queryClient.prefetchQuery(conversationQuery(id));
      onConversation(id);
    });
  };

  const decide = (action: AssistantAction, confirm: boolean) =>
    conversationId && void run(conversationId, `/api/v1/assistant/conversations/${conversationId}/actions/${action.id}/${confirm ? 'confirm' : 'decline'}`, undefined, null);

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void send(draft);
    }
  };

  const messages = detail.data?.messages ?? [];
  // Once the refetched conversation holds the answer, the live copy would only show it twice.
  const saved = new Set(messages.map((message) => message.id));
  const persisted = live.segments.some((segment) => segment.message && saved.has(segment.message.id));
  // A refetch while the answer streams (say, on window focus) already holds the question: the live copy shows it until
  // the answer is saved too, so nothing shows twice.
  const shownMessages = baseline !== null && !persisted ? messages.slice(0, baseline) : messages;
  // Earlier steps are done; only the latest one is still being revealed.
  const earlier = live.segments.slice(0, -1);
  const current = live.segments[live.segments.length - 1];

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div ref={scroller.scroll} onScroll={scroller.onScroll} className="min-h-0 flex-1 overflow-y-auto" aria-live="polite">
      <div ref={scroller.content} className="flex flex-col gap-4 p-4">
        {conversationId && detail.isPending && <LoadingState />}
        {detail.isError && <ErrorState onRetry={() => void detail.refetch()} />}
        {!conversationId && !live.question && (
          <>
            {intro?.((text) => void send(text))}
            <div className="flex flex-col items-center gap-2 py-6 text-center">
              <Bot size={24} strokeWidth={1.5} aria-hidden />
              <Typography variant="body2" color="textSecondary">{t(issueId ? 'assistant.emptyIssue' : 'assistant.empty')}</Typography>
            </div>
          </>
        )}
        {shownMessages.map((message) =>
          message.role === 'user'
            ? <UserBubble key={message.id} text={message.text ?? ''} />
            : <AssistantBubble key={message.id} message={message} busy={busy} onDecide={decide} />,
        )}
        {live.question && !persisted && <UserBubble text={live.question} />}
        {!persisted && earlier.map((segment, index) => (
          segment.message
            ? <AssistantBubble key={segment.message.id ?? index} message={segment.message} busy={busy} onDecide={decide} />
            : null
        ))}
        {live.tools.length > 0 && busy && !persisted && (
          <div className="flex flex-wrap gap-1">
            {live.tools.map((tool, index) => (
              <Badge key={index} tone={tool.state === 'failed' ? 'danger' : 'subtle'}>
                {t(tool.state === 'running' ? 'assistant.runningTool' : 'assistant.usedTool', { tool: tool.name })}
              </Badge>
            ))}
          </div>
        )}
        {current && !persisted && (
          <StreamingAnswer
            key={live.segments.length}
            segment={current}
            finished={ending !== null}
            skip={skip}
            busy={busy}
            onDecide={decide}
            onCaughtUp={setRevealed}
          />
        )}
        {busy && !ending && !current?.text && (
          <div className="flex items-center gap-2" role="status">
            <LoaderCircle size={16} strokeWidth={1.5} className="animate-spin" aria-hidden />
            <Typography variant="body2" color="textSecondary">{t('assistant.thinking')}</Typography>
          </div>
        )}
        {error && (
          <Typography role="alert" variant="body2" sx={{ color: sem('feedback-danger-text') }}>{error}</Typography>
        )}
      </div>
      </div>
      <div className="px-3 pb-3 pt-1">
        <Box
          className="flex flex-col gap-1 py-2 pl-3 pr-2"
          // Anywhere in the box but its buttons puts the cursor in the question.
          onClick={(event) => {
            if (!(event.target as HTMLElement).closest('button, textarea')) {
              input.current?.focus();
            }
          }}
          sx={{
            cursor: 'text',
            border: `1px solid ${sem('border-default')}`,
            borderRadius: `${radius.card}px`,
            backgroundColor: sem('bg-surface'),
            '&:focus-within': { borderColor: sem('border-focus') },
          }}
        >
          <InputBase
            multiline
            minRows={1}
            maxRows={5}
            placeholder={t('assistant.placeholder')}
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={onKeyDown}
            inputRef={input}
            inputProps={{ 'aria-label': t('assistant.question') }}
            sx={{ padding: 0, fontSize: font.size.md, lineHeight: 1.5, '& textarea': { overflowY: 'auto' } }}
          />
          <div className="flex justify-end">
            {busy ? (
              <IconButton
                size="small"
                aria-label={t('assistant.stop')}
                onClick={() => {
                  // Stopping also shows at once whatever had arrived.
                  setSkip(true);
                  abort.current?.abort();
                }}
                sx={{ width: 28, height: 28, backgroundColor: sem('text-primary'), color: sem('bg-surface'), '&:hover': { backgroundColor: sem('text-primary') } }}
              >
                <Square size={12} strokeWidth={1.5} fill="currentColor" aria-hidden />
              </IconButton>
            ) : (
              <IconButton
                size="small"
                aria-label={t('assistant.send')}
                disabled={!draft.trim()}
                onClick={() => void send(draft)}
                sx={{
                  width: 28,
                  height: 28,
                  backgroundColor: sem('accent-default'),
                  color: sem('text-on-accent'),
                  '&:hover': { backgroundColor: sem('accent-hover') },
                  '&.Mui-disabled': { backgroundColor: sem('bg-subtle'), color: sem('text-tertiary') },
                }}
              >
                <ArrowUp size={16} strokeWidth={1.5} aria-hidden />
              </IconButton>
            )}
          </div>
        </Box>
      </div>
    </div>
  );
}
