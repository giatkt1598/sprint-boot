import { useQuery } from '@tanstack/react-query';
import { NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react';
import { useLayoutEffect, useState, type RefObject } from 'react';
import { createPortal } from 'react-dom';
import { apiClient, ApiError, unwrap } from '../../api';
import { StatusBadge } from '../../components/Badge';
import { TicketTypeIcon, type TicketType } from '../../components/TicketIcons';
import { statusCategory } from './api';
import type { TicketRef } from './ticketMention';

const TICKET_TYPES = new Set(['story', 'task', 'bug', 'subtask', 'epic']);

// The same query the ticket page uses, so a chip and an open ticket share what they know.
const useTicketByKey = (key: string) =>
  useQuery({
    queryKey: ['tickets', key],
    queryFn: () => unwrap(apiClient.GET('/api/v1/ticket/by-key', { params: { query: { key } } })),
    retry: (count, failure) => !(failure instanceof ApiError && failure.status < 500) && count < 2,
    enabled: key.length > 0,
    staleTime: 30_000,
  });

/** The ticket an address opens, for turning a pasted ticket link into a chip; null when it is none the person sees. */
export async function resolveTicketUrl(url: string, key: string): Promise<TicketRef | null> {
  try {
    const ticket = await unwrap(apiClient.GET('/api/v1/ticket/by-key', { params: { query: { key } } }));
    return { key: ticket.key ?? key, title: ticket.title ?? '', url: `${new URL(url).origin}/browse/${ticket.key ?? key}` };
  } catch {
    return null;
  }
}

/** What a ticket chip shows: type, key, title and status as they are now. Without access, what the text saved. */
export function TicketChip({ ticketKey, savedTitle }: { ticketKey: string; savedTitle: string }) {
  const { data: ticket } = useTicketByKey(ticketKey);
  const icon = ticket?.type?.icon;
  const done = ticket ? statusCategory(ticket.status) === 'done' : false;

  return (
    <>
      {icon && TICKET_TYPES.has(icon) && <TicketTypeIcon type={icon as TicketType} />}
      <span className="ticket-mention-key" style={done ? { textDecoration: 'line-through' } : undefined}>{ticketKey}</span>
      <span className="ticket-mention-title">{ticket?.title ?? savedTitle}</span>
      {ticket?.status && <StatusBadge status={statusCategory(ticket.status)} label={ticket.status.name} />}
    </>
  );
}

/** The chip while editing; clicks select it rather than open the ticket. */
export function TicketChipView({ node }: ReactNodeViewProps) {
  const ticketKey = String(node.attrs.ticketKey ?? '');
  return (
    <NodeViewWrapper as="span" className="ticket-mention" data-ticket-key={ticketKey}>
      <TicketChip ticketKey={ticketKey} savedTitle={String(node.attrs.title ?? '')} />
    </NodeViewWrapper>
  );
}

/** Shows the ticket chips of saved HTML live, each rendered into its own link in place of the saved text. */
export function TicketChips({ container, html }: { container: RefObject<HTMLElement | null>; html: string }) {
  const [chips, setChips] = useState<{ element: HTMLElement; ticketKey: string; savedTitle: string }[]>([]);

  useLayoutEffect(() => {
    // Most text has no ticket chips; it renders exactly as before.
    if (!html.includes('ticket-mention')) {
      setChips((current) => (current.length === 0 ? current : []));
      return;
    }
    const found = [...(container.current?.querySelectorAll<HTMLElement>('a.ticket-mention[data-ticket-key]') ?? [])].map((element) => {
      const ticketKey = element.dataset.ticketKey ?? '';
      const text = element.textContent ?? '';
      const savedTitle = (text.startsWith(ticketKey) ? text.slice(ticketKey.length) : text).trim();
      element.textContent = '';
      return { element, ticketKey, savedTitle };
    });
    setChips(found);
  }, [container, html]);

  return chips.map((chip, index) => createPortal(<TicketChip ticketKey={chip.ticketKey} savedTitle={chip.savedTitle} />, chip.element, `${index}-${chip.ticketKey}`));
}
