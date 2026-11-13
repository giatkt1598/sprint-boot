import Autocomplete from '@mui/material/Autocomplete';
import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import MuiLink from '@mui/material/Link';
import MuiTextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { Button } from '../../components/Button';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { AddIcon, CloseIcon, DocumentIcon } from '../../components/icons';
import { TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { font, sem } from '../../theme/tokens';
import { DocLinkSource, useTicketDocs, useTicketDocsActions, type DocsPageHit, type TicketDocPage } from './api';

const SEARCH_DELAY = 250;
// The ticket's text links settle a moment after a description or comment is saved.
const POLL_MILLISECONDS = 10_000;

/** Waits for a pause in typing before searching. */
function useDebounced(value: string) {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = setTimeout(() => setSettled(value.trim()), SEARCH_DELAY);
    return () => clearTimeout(timer);
  }, [value]);
  return settled;
}

/** A page picked from the search, or whatever was typed: an address pasted whole. */
type Picked = DocsPageHit | string;

/** Finds Docmost pages by title, or takes a pasted page address, like the ticket picker of Linked tickets. */
function PageField({ label, value, onInput, onPick, search, autoFocus }: {
  label: string;
  value: string;
  onInput: (text: string) => void;
  onPick: (page: DocsPageHit | null) => void;
  search: (query: string) => Promise<DocsPageHit[]>;
  autoFocus?: boolean;
}) {
  const id = useId();
  const query = useDebounced(value);
  // A pasted address is not a search.
  const searching = query.length > 1 && !/^https?:\/\//i.test(query);
  const found = useQuery({
    queryKey: ['docs', 'search', label, query],
    enabled: searching,
    staleTime: 30_000,
    retry: false,
    queryFn: () => search(query),
  });

  return (
    <div className="flex min-w-0 flex-1 flex-col gap-1">
      <label htmlFor={id} style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>{label}</label>
      <Autocomplete<DocsPageHit, false, false, true>
        id={id}
        size="small"
        freeSolo
        options={searching ? (found.data ?? []) : []}
        loading={found.isFetching}
        filterOptions={(list) => list}
        inputValue={value}
        onInputChange={(_, text, reason) => {
          if (reason !== 'reset') {
            onInput(text);
            onPick(null);
          }
        }}
        onChange={(_, picked: Picked | null) => {
          if (picked && typeof picked !== 'string') {
            onInput(picked.title ?? '');
            onPick(picked);
          }
        }}
        getOptionLabel={(option) => (typeof option === 'string' ? option : (option.title ?? ''))}
        renderOption={({ key, ...props }, option) => (
          <li key={key} {...props}>
            <span className="flex min-w-0 items-center gap-2">
              <DocumentIcon size={16} strokeWidth={1.5} aria-hidden className="shrink-0" />
              <span className="truncate">{option.title}</span>
              {option.spaceName && <span className="shrink-0" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{option.spaceName}</span>}
            </span>
          </li>
        )}
        renderInput={(params) => <MuiTextField {...params} autoFocus={autoFocus} />}
      />
    </div>
  );
}

function AddDocumentRow({ ticketId, onClose }: { ticketId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const { link } = useTicketDocsActions(ticketId);
  const [text, setText] = useState('');
  const [picked, setPicked] = useState<DocsPageHit | null>(null);
  const ready = Boolean(picked || text.trim()) && !link.isPending;

  const search = (query: string) =>
    unwrap(apiClient.GET('/api/v1/docs-ticket/search-pages/{ticketId}', { params: { path: { ticketId }, query: { query } } }));

  const submit = () =>
    link.mutate(picked ? { pageId: picked.id! } : { url: text.trim() }, {
      onSuccess: () => {
        setText('');
        setPicked(null);
        onClose();
      },
      onError: (failure) => toast('error', toMessage(failure)),
    });

  return (
    <div
      className="flex flex-col gap-2 px-2 py-2"
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          onClose();
        }
      }}
    >
      <PageField label={t('docs.findPage')} value={text} onInput={setText} onPick={setPicked} search={search} autoFocus />
      <div className="flex justify-end gap-2">
        <Button kind="ghost" onClick={onClose}>{t('common.cancel')}</Button>
        <Button disabled={!ready} onClick={submit}>{t('docs.linkPage')}</Button>
      </div>
    </div>
  );
}

/** The new page's title and where it goes in the space's Docmost space; the top level unless a parent is picked. */
function CreateDocumentDialog({ ticketId, projectId, defaultTitle, onClose }: {
  ticketId: string;
  projectId: string;
  defaultTitle: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const { createPage } = useTicketDocsActions(ticketId);
  const [title, setTitle] = useState(defaultTitle);
  const [parentText, setParentText] = useState('');
  const [parent, setParent] = useState<DocsPageHit | null>(null);

  const search = (query: string) =>
    unwrap(apiClient.GET('/api/v1/docs-project/search-pages/{projectId}', { params: { path: { projectId }, query: { query } } }));

  const create = () =>
    createPage.mutate(
      { title: title.trim(), parentPageId: parent?.id ?? undefined },
      {
        onSuccess: (page) => {
          toast('success', t('docs.pageCreated'));
          onClose();
          window.open(page.url ?? '', '_blank', 'noopener,noreferrer');
        },
      },
    );

  return (
    <Dialog
      open
      title={t('docs.createDocument')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('docs.createDocument')}
          confirmDisabled={!title.trim() || Boolean(parentText.trim() && !parent) || createPage.isPending}
          onCancel={onClose}
          onConfirm={create}
        />
      }
    >
      <TextField label={t('docs.pageTitle')} autoFocus value={title} onChange={(event) => setTitle(event.target.value)} />
      <div className="flex flex-col gap-1">
        <PageField label={t('docs.parentPage')} value={parentText} onInput={setParentText} onPick={setParent} search={search} />
        <Typography variant="body2" color="textSecondary">{parent ? t('docs.underParent', { title: parent.title }) : t('docs.topLevel')}</Typography>
      </div>
      {createPage.isError && <Typography role="alert" color="error" variant="body2">{toMessage(createPage.error)}</Typography>}
    </Dialog>
  );
}

function DocumentRows({ pages, label, canRemove, onRemove, removing }: {
  pages: TicketDocPage[];
  label: string;
  canRemove?: (page: TicketDocPage) => boolean;
  onRemove?: (page: TicketDocPage) => void;
  removing?: boolean;
}) {
  const { t } = useTranslation();

  return (
    <table className="w-full" aria-label={label} style={{ borderCollapse: 'collapse', borderTop: `1px solid ${sem('border-default')}` }}>
      <tbody>
        {pages.map((page) => (
          <Box
            component="tr"
            key={page.id}
            sx={{ height: 36, borderBottom: `1px solid ${sem('border-default')}`, '&:hover': { backgroundColor: sem('bg-hover') } }}
          >
            <td className="pl-2 pr-3"><span className="flex" style={{ color: sem('text-secondary') }}><DocumentIcon size={16} strokeWidth={1.5} aria-hidden /></span></td>
            <td className="w-full pr-3" style={{ maxWidth: 0 }}>
              <MuiLink href={page.url ?? ''} target="_blank" rel="noopener noreferrer" color="inherit" variant="body2" className="block truncate" title={page.title ?? undefined}>
                {page.title}
              </MuiLink>
            </td>
            <td className="whitespace-nowrap pr-3">
              <Typography variant="body2" color="textSecondary">{page.spaceName}</Typography>
            </td>
            {canRemove && (
              <td className="pr-2" style={{ width: 40 }}>
                {canRemove(page) ? (
                  <IconButton size="small" aria-label={t('docs.unlinkPage', { title: page.title })} disabled={removing} onClick={() => onRemove?.(page)}>
                    <CloseIcon size={16} strokeWidth={1.5} aria-hidden />
                  </IconButton>
                ) : (
                  <span className="sr-only">{t('docs.sourceText')}</span>
                )}
              </td>
            )}
          </Box>
        ))}
      </tbody>
    </table>
  );
}

/**
 * Linked documents, above Attachments: pages linked by hand or by a chip in the ticket's text. Below it, Mentions:
 * Docmost pages that link to the ticket, shown only when there are some.
 */
export function LinkedDocuments({ ticketId, projectId, defaultTitle }: { ticketId: string; projectId: string; defaultTitle: string }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const docs = useTicketDocs(ticketId, POLL_MILLISECONDS);
  const { unlink } = useTicketDocsActions(ticketId);
  const [adding, setAdding] = useState(false);
  const [creating, setCreating] = useState(false);
  const data = docs.data;

  // No Docmost connection, or a failed read that must not disturb the ticket.
  if (!data?.configured) {
    return null;
  }
  const linked = data.linked ?? [];
  const mentions = data.mentions ?? [];
  if (!data.canEdit && linked.length === 0 && mentions.length === 0) {
    return null;
  }

  return (
    <>
      <section className="flex flex-col" aria-labelledby={`docs-${ticketId}`}>
        <div className="flex items-center gap-3 pb-2">
          <Typography id={`docs-${ticketId}`} variant="h2" component="h2" className="flex-1">{t('docs.linkedTitle')}</Typography>
          {data.canEdit && data.canCreatePage && (
            <Button kind="link" onClick={() => setCreating(true)}>{t('docs.createDocument')}</Button>
          )}
          {data.canEdit && (
            <Button kind="link" startIcon={<AddIcon size={16} strokeWidth={1.5} aria-hidden />} onClick={() => setAdding(true)}>{t('docs.add')}</Button>
          )}
        </div>
        {linked.length > 0 && (
          <DocumentRows
            pages={linked}
            label={t('docs.linkedTitle')}
            canRemove={data.canEdit ? (page) => page.source === DocLinkSource.manual : undefined}
            removing={unlink.isPending}
            onRemove={(page) => unlink.mutate(page.id!, { onError: (failure) => toast('error', toMessage(failure)) })}
          />
        )}
        {linked.length === 0 && !adding && <Typography variant="body2" color="textSecondary" className="px-2">{t('docs.linkedEmpty')}</Typography>}
        {adding && <AddDocumentRow ticketId={ticketId} onClose={() => setAdding(false)} />}
      </section>
      {mentions.length > 0 && (
        <section className="flex flex-col" aria-labelledby={`docs-mentions-${ticketId}`}>
          <div className="pb-2">
            <Typography id={`docs-mentions-${ticketId}`} variant="h2" component="h2">{t('docs.mentionsTitle')}</Typography>
            <Typography variant="body2" color="textSecondary">{t('docs.mentionsHint')}</Typography>
          </div>
          <DocumentRows pages={mentions} label={t('docs.mentionsTitle')} />
        </section>
      )}
      {creating && <CreateDocumentDialog ticketId={ticketId} projectId={projectId} defaultTitle={defaultTitle} onClose={() => setCreating(false)} />}
    </>
  );
}

