import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDateTime } from '../../lib/format';
import { font, radius, sem } from '../../theme/tokens';
import { assistantKeys, useAssistantStatus, useConversations, type Conversation } from './api';
import { ChatThread } from './ChatThread';

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
export function AssistantPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const { conversationId } = useParams();
  const status = useAssistantStatus();
  const list = useConversations();
  const [renaming, setRenaming] = useState<Conversation | null>(null);
  const [removing, setRemoving] = useState<Conversation | null>(null);
  const selected = list.data?.find((item) => item.id === conversationId);

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
    <div className="-m-8 flex h-[calc(100%+4rem)] min-h-0">
      <aside className="flex w-72 shrink-0 flex-col gap-2 overflow-y-auto p-4" style={{ borderRight: `1px solid ${sem('border-default')}` }}>
        <div className="flex items-center gap-2">
          <Typography variant="h2" component="h1" className="flex-1">{t('assistant.title')}</Typography>
          <Button kind="secondary" startIcon={<Plus size={16} strokeWidth={1.5} aria-hidden />} onClick={() => void navigate('/assistant')}>
            {t('assistant.new')}
          </Button>
        </div>
        {list.isPending && <LoadingState />}
        {list.isError && <ErrorState onRetry={() => void list.refetch()} />}
        {list.data?.length === 0 && <Typography variant="body2" color="textSecondary">{t('assistant.noConversations')}</Typography>}
        <nav aria-label={t('assistant.conversations')} className="flex flex-col gap-1">
          {(list.data ?? []).map((item) => (
            <div
              key={item.id}
              className="group flex items-center gap-1 pr-1"
              style={{ borderRadius: radius.control, backgroundColor: item.id === conversationId ? sem('bg-selected') : undefined }}
            >
              <Link
                to={`/assistant/${item.id}`}
                className="flex min-w-0 flex-1 flex-col px-2 py-1"
                style={{ textDecoration: 'none', color: sem('text-primary') }}
                aria-current={item.id === conversationId ? 'page' : undefined}
              >
                <span className="truncate" style={{ fontSize: font.size.md }}>{item.title || t('assistant.untitled')}</span>
                <span className="flex items-center gap-1" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>
                  {item.issueKey && <Badge tone="subtle">{item.issueKey}</Badge>}
                  {formatDateTime(item.updatedAt, i18n.language)}
                </span>
              </Link>
              <IconButton size="small" aria-label={t('assistant.rename', { title: item.title })} onClick={() => setRenaming(item)}>
                <Pencil size={16} strokeWidth={1.5} aria-hidden />
              </IconButton>
              <IconButton size="small" aria-label={t('assistant.delete', { title: item.title })} onClick={() => setRemoving(item)}>
                <Trash2 size={16} strokeWidth={1.5} aria-hidden />
              </IconButton>
            </div>
          ))}
        </nav>
      </aside>
      <section className="flex min-w-0 flex-1 flex-col" aria-label={selected?.title || t('assistant.new')}>
        {selected?.issueKey && (
          <div className="px-4 py-2" style={{ borderBottom: `1px solid ${sem('border-default')}` }}>
            <Typography variant="body2" color="textSecondary">
              {t('assistant.aboutIssue')} <Link to={`/issues/${selected.issueKey}`}>{selected.issueKey}</Link>
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
