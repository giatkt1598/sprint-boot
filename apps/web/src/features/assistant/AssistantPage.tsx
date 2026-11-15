import IconButton from '@mui/material/IconButton';
import Menu from '@mui/material/Menu';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { AddIcon, DeleteIcon, EditIcon, MoreIcon } from '../../components/icons';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { SideNav } from '../../layout/ShellNav';
import { Link, useNavigate, useParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { MenuItem } from '../../components/Menu';
import { Resizer, usePersistentWidth } from '../../components/Resizer';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDateTime } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';
import { assistantKeys, useAssistantStatus, useConversations, type Conversation } from './api';
import { ChatThread } from './ChatThread';
import { usePageTitle } from '../../lib/pageTitle';

function RenameDialog({ conversation, onClose }: { conversation: Conversation; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [title, setTitle] = useState(conversation.title ?? '');

  const rename = useMutation({
    mutationFn: () => unwrap(apiClient.POST('/api/v1/assistant/{id}/rename', { params: { path: { id: conversation.id! } }, body: { title } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['assistant'] });
      onClose();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  return (
    <Dialog
      open
      title={t('assistant.renameTitle')}
      onClose={onClose}
      footer={<DialogFooter confirmLabel={t('common.save')} confirmDisabled={!title.trim() || rename.isPending} onCancel={onClose} onConfirm={() => rename.mutate()} />}
    >
      <TextField label={t('assistant.conversationTitle')} autoFocus value={title} onChange={(event) => setTitle(event.target.value)} />
    </Dialog>
  );
}

/** Every conversation of the person, with the open one beside the list. */
const LIST_WIDTH = { min: 280, max: 560, initial: 360, maxShare: 0.4 };

/** One conversation in the list. Its actions sit behind a vertical "more" button that shows on hover or focus. */
function ConversationRow({ item, current, onRename, onDelete }: { item: Conversation; current: boolean; onRename: () => void; onDelete: () => void }) {
  const { t, i18n } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const title = item.title || t('assistant.untitled');
  const run = (action: () => void) => {
    setAnchor(null);
    action();
  };

  return (
    <div
      className="group flex items-center gap-1 pr-1"
      style={{ borderRadius: radius.control, backgroundColor: current ? sem('bg-selected') : undefined }}
    >
      <Link
        to={`/assistant/${item.id}`}
        className="flex min-w-0 flex-1 flex-col px-2 py-1"
        style={{ textDecoration: 'none', color: sem('text-primary') }}
        aria-current={current ? 'page' : undefined}
      >
        <span className="truncate" style={{ fontSize: font.size.md }}>{title}</span>
        <span className="flex items-center gap-1" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>
          {item.ticketKey && <Badge tone="subtle">{item.ticketKey}</Badge>}
          {formatDateTime(item.updatedAt, i18n.language)}
        </span>
      </Link>
      <IconButton
        size="small"
        aria-label={t('assistant.actions', { title })}
        aria-haspopup="menu"
        aria-expanded={anchor ? true : undefined}
        onClick={(event) => setAnchor(event.currentTarget)}
        sx={{
          opacity: anchor ? 1 : 0,
          '.group:hover &, .group:focus-within &': { opacity: 1 },
          '@media (hover: none)': { opacity: 1 },
        }}
      >
        {/* The registry has no vertical "more" icon, so the horizontal one is turned upright. */}
        <MoreIcon size={16} strokeWidth={1.5} aria-hidden style={{ transform: 'rotate(90deg)' }} />
      </IconButton>
      <Menu open={Boolean(anchor)} anchorEl={anchor} onClose={() => setAnchor(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }} transformOrigin={{ vertical: 'top', horizontal: 'right' }}>
        <MenuItem icon={EditIcon} onClick={() => run(onRename)}>{t('common.edit')}</MenuItem>
        <MenuItem icon={DeleteIcon} danger onClick={() => run(onDelete)}>{t('common.delete')}</MenuItem>
      </Menu>
    </div>
  );
}

export function AssistantPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const { conversationId } = useParams();
  const status = useAssistantStatus();
  const list = useConversations();
  const [renaming, setRenaming] = useState<Conversation | null>(null);
  const [removing, setRemoving] = useState<Conversation | null>(null);
  const listWidth = usePersistentWidth('sprint-boot.assistant-list-width', LIST_WIDTH);
  const selected = list.data?.find((item) => item.id === conversationId);
  usePageTitle(selected ? selected.title || t('assistant.untitled') : null, t('assistant.title'));

  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/assistant/{id}', { params: { path: { id } } })),
    onSuccess: async (_, id) => {
      setRemoving(null);
      await queryClient.invalidateQueries({ queryKey: assistantKeys.list() });
      if (id === conversationId) {
        await navigate('/assistant');
      }
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  if (status.data && !status.data.available) {
    return <EmptyState title={t('assistant.unavailable')} />;
  }

  return (
    <div className="-m-4 flex h-[calc(100%+2rem)] min-h-0 md:-m-8 md:h-[calc(100%+4rem)]">
      {/* The conversation list sits beside the chat on wide screens and in the menu drawer on small ones. */}
      <SideNav label={t('assistant.conversations')}>
      {(inDrawer) => (
      <>
      <aside className="flex shrink-0 flex-col gap-2 overflow-y-auto p-4" style={inDrawer ? undefined : { width: listWidth.width, borderRight: `1px solid ${sem('border-default')}` }}>
        <div className="flex items-center gap-2">
          <Typography variant="h2" component="h1" className="flex-1">{t('assistant.title')}</Typography>
          <Button kind="secondary" startIcon={<AddIcon size={16} strokeWidth={1.5} aria-hidden />} onClick={() => void navigate('/assistant')}>
            {t('assistant.new')}
          </Button>
        </div>
        {list.isPending && <LoadingState />}
        {list.isError && <ErrorState onRetry={() => void list.refetch()} />}
        {list.data?.length === 0 && <Typography variant="body2" color="textSecondary">{t('assistant.noConversations')}</Typography>}
        <nav aria-label={t('assistant.conversations')} className="flex flex-col gap-1">
          {(list.data ?? []).map((item) => (
            <ConversationRow key={item.id} item={item} current={item.id === conversationId} onRename={() => setRenaming(item)} onDelete={() => setRemoving(item)} />
          ))}
        </nav>
      </aside>
      {!inDrawer && <Resizer edge="end" label={t('assistant.resizeList')} {...listWidth} onChange={listWidth.change} />}
      </>
      )}
      </SideNav>
      <section className="flex min-w-0 flex-1 flex-col" aria-label={selected?.title || t('assistant.new')}>
        {selected?.ticketKey && (
          <div className="px-4 py-2" style={{ borderBottom: `1px solid ${sem('border-default')}` }}>
            <Typography variant="body2" color="textSecondary">
              {t('assistant.aboutTicket')} <Link to={`/browse/${selected.ticketKey}`}>{selected.ticketKey}</Link>
            </Typography>
          </div>
        )}
        <ChatThread
          key={conversationId ?? 'new'}
          conversationId={conversationId ?? null}
          onConversation={(id) => void navigate(`/assistant/${id}`, { replace: true })}
        />
      </section>
      {renaming && <RenameDialog conversation={renaming} onClose={() => setRenaming(null)} />}
      <DialogConfirm
        open={removing !== null}
        title={t('assistant.deleteTitle', { title: removing?.title })}
        description={t('assistant.deleteBody')}
        confirmLabel={t('common.delete')}
        onCancel={() => setRemoving(null)}
        onConfirm={() => removing && remove.mutate(removing.id!)}
      />
    </div>
  );
}
