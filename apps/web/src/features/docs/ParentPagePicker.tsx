import ButtonBase from '@mui/material/ButtonBase';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient, unwrap } from '../../api';
import { ChevronDownIcon, ChevronRightIcon, DocumentIcon } from '../../components/icons';
import { SearchInput } from '../../components/SearchInput';
import { LoadingState } from '../../components/StateViews';
import { font, radius, sem } from '../../theme/tokens';
import { useDocsPages, type DocsPageNode } from './api';

const SEARCH_DELAY = 250;
const INDENT = 20;

/** The page a new page goes under; null is the top of the space. */
export interface ParentPage {
  id: string;
  title: string;
}

function Row({ label, icon, depth, selected, onSelect, toggle }: {
  label: string;
  icon?: React.ReactNode;
  depth: number;
  selected: boolean;
  onSelect: () => void;
  toggle?: React.ReactNode;
}) {
  return (
    <div className="flex min-h-8 items-center" style={{ paddingLeft: depth * INDENT }}>
      <span className="flex w-7 shrink-0 justify-center">{toggle}</span>
      <ButtonBase
        role="option"
        aria-selected={selected}
        onClick={onSelect}
        className="flex min-h-8 min-w-0 flex-1 items-center gap-2 px-2"
        sx={{
          justifyContent: 'flex-start',
          borderRadius: `${radius.control}px`,
          fontSize: font.size.md,
          color: sem('text-primary'),
          backgroundColor: selected ? sem('bg-selected') : undefined,
          '&:hover': { backgroundColor: selected ? sem('bg-selected') : sem('bg-hover') },
        }}
      >
        <span aria-hidden className="flex shrink-0" style={{ color: sem('text-secondary') }}>{icon ?? <DocumentIcon size={16} strokeWidth={1.5} />}</span>
        <span className="truncate">{label}</span>
      </ButtonBase>
    </div>
  );
}

function TreeNode({ projectId, page, depth, selectedId, onSelect }: {
  projectId: string;
  page: DocsPageNode;
  depth: number;
  selectedId: string | null;
  onSelect: (page: ParentPage) => void;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const children = useDocsPages(projectId, page.id ?? undefined, open);
  const Chevron = open ? ChevronDownIcon : ChevronRightIcon;

  return (
    <>
      <Row
        label={page.title ?? ''}
        icon={page.icon ?? undefined}
        depth={depth}
        selected={selectedId === page.id}
        onSelect={() => onSelect({ id: page.id!, title: page.title ?? '' })}
        toggle={page.hasChildren && (
          <IconButton size="small" aria-expanded={open} aria-label={t(open ? 'docs.collapse' : 'docs.expand', { title: page.title })} onClick={() => setOpen(!open)}>
            <Chevron size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
        )}
      />
      {open && children.isPending && <div style={{ paddingLeft: (depth + 1) * INDENT + 28 }}><LoadingState /></div>}
      {open && children.data?.map((child) => (
        <TreeNode key={child.id} projectId={projectId} page={child} depth={depth + 1} selectedId={selectedId} onSelect={onSelect} />
      ))}
    </>
  );
}

/**
 * Where a new page goes, as Docmost asks when using a template: the space's page tree, opened level by level, with a
 * search that lists matching pages instead.
 */
export function ParentPagePicker({ projectId, value, onChange }: { projectId: string; value: ParentPage | null; onChange: (page: ParentPage | null) => void }) {
  const { t } = useTranslation();
  const labelId = useId();
  const [text, setText] = useState('');
  const [query, setQuery] = useState('');
  useEffect(() => {
    const timer = setTimeout(() => setQuery(text.trim()), SEARCH_DELAY);
    return () => clearTimeout(timer);
  }, [text]);

  const roots = useDocsPages(projectId, undefined, !query);
  const found = useQuery({
    queryKey: ['docs', 'project', projectId, 'parent-search', query],
    enabled: query.length > 0,
    staleTime: 30_000,
    retry: false,
    queryFn: () => unwrap(apiClient.GET('/api/v1/docs-project/search-pages/{projectId}', { params: { path: { projectId }, query: { query } } })),
  });
  const pending = query ? found.isPending : roots.isPending;
  const empty = query ? found.data?.length === 0 : roots.data?.length === 0;

  return (
    <div className="flex flex-col gap-2">
      <span id={labelId} style={{ fontSize: font.size.xs, fontWeight: font.weight.medium, color: sem('text-secondary') }}>{t('docs.parentPage')}</span>
      <SearchInput label={t('docs.searchPages')} width="100%" value={text} onChange={(event) => setText(event.target.value)} />
      <div
        role="listbox"
        aria-labelledby={labelId}
        className="flex flex-col overflow-y-auto p-1"
        style={{ height: 240, border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}
      >
        {!query && (
          <Row label={t('docs.topLevelOption')} depth={0} selected={value === null} onSelect={() => onChange(null)} />
        )}
        {pending && <div className="p-2"><LoadingState /></div>}
        {(found.isError || roots.isError) && <Typography variant="body2" color="error" className="p-2">{t('errors.DocsUnavailable')}</Typography>}
        {empty && <Typography variant="body2" color="textSecondary" className="p-2">{t(query ? 'docs.noMatches' : 'docs.noPages')}</Typography>}
        {query
          ? found.data?.map((page) => (
              <Row key={page.id} label={page.title ?? ''} depth={0} selected={value?.id === page.id} onSelect={() => onChange({ id: page.id!, title: page.title ?? '' })} />
            ))
          : roots.data?.map((page) => (
              <TreeNode key={page.id} projectId={projectId} page={page} depth={0} selectedId={value?.id ?? null} onSelect={onChange} />
            ))}
      </div>
      <Typography variant="body2" color="textSecondary">{value ? t('docs.underParent', { title: value.title }) : t('docs.topLevel')}</Typography>
    </div>
  );
}
