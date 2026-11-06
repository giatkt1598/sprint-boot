import Autocomplete from '@mui/material/Autocomplete';
import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import MuiTextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Lock, Plus, X } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { Button } from '../../components/Button';
import { IssueTypeIcon, type IssueType as IconType } from '../../components/IssueIcons';
import { SelectField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { font, sem } from '../../theme/tokens';
import type { Issue, IssueSummary } from './api';

type IssueLink = components['schemas']['IssueLinkDto'];

const SEARCH_DELAY = 250;
const MAX_SUGGESTIONS = 8;

export const issueLinksKey = (issueId: string | undefined) => ['issues', 'links', issueId];

export const useLinkTypes = () =>
  useQuery({
    queryKey: ['issue-link-types'],
    staleTime: 60_000,
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue-link-type')),
  });

/** Suggests issues from every space the person can see, by key or by words in the title. */
function IssueKeyField({ label, value, onChange, excludeId }: { label: string; value: string; onChange: (key: string) => void; excludeId?: string }) {
  const id = useId();
  const [search, setSearch] = useState(value.trim());

  useEffect(() => {
    const timer = setTimeout(() => setSearch(value.trim()), SEARCH_DELAY);
    return () => clearTimeout(timer);
  }, [value]);

  const found = useQuery({
    queryKey: ['issues', 'search', 'link-picker', search],
    enabled: search.length > 0,
    staleTime: 30_000,
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue/search', { params: { query: { Text: search, MaxResultCount: MAX_SUGGESTIONS } } })),
  });
  const options = (found.data?.items ?? []).filter((item) => item.id !== excludeId);

  return (
    <div className="flex min-w-0 flex-1 flex-col gap-1">
      <label htmlFor={id} style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>
        {label}
      </label>
      <Autocomplete<IssueSummary, false, false, true>
        id={id}
        size="small"
        freeSolo
        options={options}
        filterOptions={(list) => list}
        inputValue={value}
        onInputChange={(_, text, reason) => reason !== 'reset' && onChange(text)}
        onChange={(_, picked) => onChange(typeof picked === 'string' ? picked : (picked?.key ?? ''))}
        getOptionLabel={(option) => (typeof option === 'string' ? option : (option.key ?? ''))}
        renderOption={({ key, ...props }, option) => (
          <li key={key} {...props}>
            <span className="flex min-w-0 items-center gap-2">
              <IssueTypeIcon type={option.type?.icon as IconType} />
              <span className="shrink-0" style={{ fontSize: font.size.xs, color: sem('text-secondary') }}>{option.key}</span>
              <span className="truncate">{option.title}</span>
            </span>
          </li>
        )}
        renderInput={(params) => <MuiTextField {...params} autoFocus />}
      />
    </div>
  );
}

function AddLinkRow({ issue, onClose }: { issue: Issue; onClose: () => void }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const types = useLinkTypes();
  // One option per phrase: "blocks" and "is blocked by" are two ways to add the same type.
  const phrases = (types.data ?? []).flatMap((type) => [
    { value: `${type.id}:0`, label: type.outward! },
    ...(type.inward !== type.outward ? [{ value: `${type.id}:1`, label: type.inward! }] : []),
  ]);
  const [phrase, setPhrase] = useState('');
  const [key, setKey] = useState('');
  const chosen = phrase || phrases[0]?.value || '';

  const add = useMutation({
    mutationFn: () => {
      const [linkTypeId, direction] = chosen.split(':');
      return unwrap(
        apiClient.POST('/api/v1/issue-link', {
          params: { query: { issueId: issue.id! } },
          body: { linkTypeId: linkTypeId!, direction: Number(direction) as 0 | 1, issueKey: key.trim() },
        }),
      );
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['issues'] });
      setKey('');
      onClose();
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const ready = Boolean(chosen && key.trim()) && !add.isPending;

  return (
    <div
      className="flex flex-col gap-2 px-2 py-2"
      onKeyDown={(event) => {
        if (event.key === 'Escape') {
          onClose();
        }
      }}
    >
      <div className="flex items-end gap-3">
        <div style={{ width: 200 }}>
          <SelectField label={t('links.relationship')} value={chosen} onChange={(event) => setPhrase(event.target.value)} options={phrases} />
        </div>
        <IssueKeyField label={t('links.issue')} value={key} excludeId={issue.id} onChange={setKey} />
      </div>
      <div className="flex justify-end gap-2">
        <Button kind="ghost" onClick={onClose}>{t('common.cancel')}</Button>
        <Button disabled={!ready} onClick={() => add.mutate()}>{t('links.link')}</Button>
      </div>
    </div>
  );
}

/** Related issues, grouped by how this issue reads: "blocks", "is blocked by", "relates to". */
export function LinkedIssues({ issue, canEdit }: { issue: Issue; canEdit: boolean }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);

  const links = useQuery({
    queryKey: issueLinksKey(issue.id),
    queryFn: () => unwrap(apiClient.GET('/api/v1/issue-link', { params: { query: { issueId: issue.id! } } })),
  });
  const remove = useMutation({
    mutationFn: (id: string) => unwrap(apiClient.DELETE('/api/v1/issue-link/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['issues'] }),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const groups = new Map<string, IssueLink[]>();
  for (const link of links.data ?? []) {
    groups.set(link.phrase ?? '', [...(groups.get(link.phrase ?? '') ?? []), link]);
  }

  if (!canEdit && groups.size === 0) {
    return null;
  }

  return (
    <section className="flex flex-col" aria-labelledby={`links-${issue.id}`}>
      <div className="flex items-center pb-2">
        <Typography id={`links-${issue.id}`} variant="h2" component="h2" className="flex-1">{t('links.title')}</Typography>
        {canEdit && (
          <Button kind="link" startIcon={<Plus size={16} strokeWidth={1.5} aria-hidden />} onClick={() => setAdding(true)}>
            {t('links.add')}
          </Button>
        )}
      </div>
      {[...groups.entries()].map(([phrase, items]) => (
        <div key={phrase} className="flex flex-col">
          <Typography variant="caption" color="textSecondary" className="px-2 pb-1 pt-2">{phrase}</Typography>
          <table className="w-full" aria-label={phrase} style={{ borderCollapse: 'collapse', borderTop: `1px solid ${sem('border-default')}` }}>
            <tbody>
              {items.map((link) => (
                <Box
                  component="tr"
                  key={link.id}
                  sx={{ height: 36, borderBottom: `1px solid ${sem('border-default')}`, '&:hover': { backgroundColor: sem('bg-hover') } }}
                >
                  {link.issue ? (
                    <>
                      <td className="pl-2 pr-3"><span className="flex"><IssueTypeIcon type={link.issue.typeIcon as IconType} /></span></td>
                      <td className="whitespace-nowrap pr-3">
                        <Link component={RouterLink} to={`/issues/${link.issue.key}`} color="inherit" variant="body2">{link.issue.key}</Link>
                      </td>
                      <td className="w-full pr-3" style={{ maxWidth: 0 }}>
                        <Typography variant="body2" className="truncate" title={link.issue.title ?? undefined}>{link.issue.title}</Typography>
                      </td>
                      <td className="whitespace-nowrap pr-3">
                        <Typography variant="body2" color="textSecondary">{link.issue.status?.name}</Typography>
                      </td>
                    </>
                  ) : (
                    <>
                      <td className="pl-2 pr-3"><span className="flex" style={{ color: sem('text-secondary') }}><Lock size={16} strokeWidth={1.5} aria-hidden /></span></td>
                      <td colSpan={3} className="w-full pr-3">
                        <Typography variant="body2" color="textSecondary">{t('links.restricted')}</Typography>
                      </td>
                    </>
                  )}
                  <td className="pr-2" style={{ width: 40 }}>
                    {canEdit && (
                      <IconButton
                        size="small"
                        aria-label={t('links.remove', { key: link.issue?.key ?? t('links.restricted') })}
                        disabled={remove.isPending}
                        onClick={() => remove.mutate(link.id!)}
                      >
                        <X size={16} strokeWidth={1.5} aria-hidden />
                      </IconButton>
                    )}
                  </td>
                </Box>
              ))}
            </tbody>
          </table>
        </div>
      ))}
      {canEdit && groups.size === 0 && !adding && (
        <Typography variant="body2" color="textSecondary" className="px-2">{t('links.empty')}</Typography>
      )}
      {adding && <AddLinkRow issue={issue} onClose={() => setAdding(false)} />}
    </section>
  );
}
