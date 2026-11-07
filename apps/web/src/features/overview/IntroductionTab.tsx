import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Pencil } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Button } from '../../components/Button';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatRelative } from '../../lib/format';
import { hasSelectedText } from '../../lib/selection';
import { font, hoverPane, radius, sem } from '../../theme/tokens';
import { RichTextEditor, RichTextView } from '../issues/RichTextEditor';
import { useMembers, type Space } from '../spaces/api';
import { introductionKey, useIntroduction } from './api';

/** The page that introduces a space. Admins edit it in place by clicking it; everyone else reads it. */
export function IntroductionTab({ space }: { space: Space }) {
  const { t, i18n } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const introduction = useIntroduction(space.id);
  const members = useMembers(space.id);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState('');

  const save = useMutation({
    mutationFn: (content: string) =>
      unwrap(apiClient.PUT('/api/v1/project-introduction', { params: { query: { projectId: space.id! } }, body: { content } })),
    onSuccess: (saved) => {
      queryClient.setQueryData(introductionKey(space.id), saved);
      setEditing(false);
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  if (introduction.isPending) {
    return <LoadingState />;
  }
  if (introduction.isError) {
    return <ErrorState onRetry={() => void introduction.refetch()} />;
  }

  const { canEdit, updatedBy, updatedAt } = introduction.data;
  const content = introduction.data.content ?? '';
  const startEditing = () => {
    setDraft(content);
    setEditing(true);
  };

  return (
    <div className="flex max-w-3xl flex-col gap-4">
      {editing ? (
        <>
          <RichTextEditor
            label={t('overview.introduction')}
            initialValue={content}
            people={(members.data ?? []).map((member) => member.user!)}
            projectId={space.id}
            autoFocus
            onChange={setDraft}
            footer={
              <>
                <Button kind="ghost" onClick={() => setEditing(false)}>{t('common.cancel')}</Button>
                <Button disabled={save.isPending} onClick={() => save.mutate(draft)}>{t('common.save')}</Button>
              </>
            }
          />
        </>
      ) : content ? (
        canEdit ? (
          <Box
            role="button"
            tabIndex={0}
            aria-label={t('overview.editIntroduction')}
            onClick={(event) => {
              // A link opens, and a drag that selected text stays a selection; only a plain click edits.
              if (!(event.target as HTMLElement).closest('a') && !hasSelectedText()) {
                startEditing();
              }
            }}
            onKeyDown={(event) => event.key === 'Enter' && startEditing()}
            sx={{
              position: 'relative',
              // The frame is always there, only coloured on hover, so the text never moves.
              margin: -2,
              padding: 2,
              border: '1px solid transparent',
              borderRadius: `${radius.card}px`,
              cursor: 'text',
              '& .edit-mark': { opacity: 0 },
              '&:hover, &:focus-visible': { ...hoverPane, borderColor: sem('border-default') },
              '&:hover .edit-mark, &:focus-visible .edit-mark': { opacity: 1 },
            }}
          >
            <span className="edit-mark absolute right-3 top-3 flex" aria-hidden style={{ color: sem('text-secondary') }}>
              <Pencil size={16} strokeWidth={1.5} />
            </span>
            <RichTextView html={content} />
          </Box>
        ) : (
          <RichTextView html={content} />
        )
      ) : canEdit ? (
        <Box
          role="button"
          tabIndex={0}
          onClick={startEditing}
          onKeyDown={(event) => event.key === 'Enter' && startEditing()}
          className="flex flex-col gap-1 p-6"
          sx={{
            border: `1px dashed ${sem('border-strong')}`,
            borderRadius: `${radius.card}px`,
            cursor: 'text',
            '&:hover': hoverPane,
          }}
        >
          <Typography variant="h3" component="span">{t('overview.writeIntroduction')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('overview.writeIntroductionHint')}</Typography>
        </Box>
      ) : (
        <EmptyState title={t('overview.noIntroduction')} />
      )}

      {updatedAt && !editing && (
        <p className="pt-3" style={{ borderTop: `1px solid ${sem('border-default')}`, fontSize: font.size.xs, color: sem('text-secondary') }}>
          {t('overview.lastEdited', { name: updatedBy?.name ?? t('common.deactivatedUser'), when: formatRelative(updatedAt, i18n.language) })}
        </p>
      )}
    </div>
  );
}
