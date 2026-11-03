import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Copy } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { CopyField } from '../../components/CopyField';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { Tabs } from '../../components/Tabs';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { formatDate, formatRelative } from '../../lib/format';
import { radius, sem } from '../../theme/tokens';

type PersonalToken = components['schemas']['PersonalTokenDto'];
type Kind = 0 | 1;

const API: Kind = 0;
const MCP: Kind = 1;
const KEY = ['account', 'tokens'];
const LIFETIMES = ['30', '90', '365', 'never'];

const MCP_PLACEHOLDER = 'YOUR_MCP_TOKEN';

const MCP_TOKEN_ENV = 'SPRINT_BOOT_MCP_TOKEN';

/**
 * How each client connects. Commands stay on one line: cmd and PowerShell do not continue a
 * line on a backslash, so a wrapped command breaks there.
 */
function mcpSetup(token: string) {
  const url = `${window.location.origin}/mcp`;
  const header = `Bearer ${token}`;
  return {
    url,
    claudeCode: `claude mcp add --transport http sprint-boot ${url} --header "Authorization: ${header}"`,
    // Codex reads the token from an environment variable rather than keeping it in its config.
    codex: [
      `setx ${MCP_TOKEN_ENV} "${token}"`,
      `codex mcp add sprint-boot --url ${url} --bearer-token-env-var ${MCP_TOKEN_ENV}`,
    ].join('\n'),
    copilot: JSON.stringify({ servers: { 'sprint-boot': { type: 'http', url, headers: { Authorization: header } } } }, null, 2),
    config: JSON.stringify({ mcpServers: { 'sprint-boot': { type: 'http', url, headers: { Authorization: header } } } }, null, 2),
  };
}

const MCP_CLIENTS = ['claudeCode', 'codex', 'copilot', 'config'] as const;
type McpClient = (typeof MCP_CLIENTS)[number];

function McpConnect({ token }: { token: string }) {
  const { t } = useTranslation();
  const [client, setClient] = useState<McpClient>('claudeCode');
  const setup = mcpSetup(token);

  return (
    <div className="flex flex-col gap-2">
      <Tabs
        label={t('mcp.clients')}
        value={client}
        onChange={(next) => setClient(next as McpClient)}
        items={MCP_CLIENTS.map((value) => ({ value, label: t(`mcp.client.${value}`) }))}
      />
      <Typography variant="body2" color="textSecondary">{t(`mcp.clientHint.${client}`, { variable: MCP_TOKEN_ENV })}</Typography>
      <CodeBlock code={setup[client]} copyLabel={t(client === 'claudeCode' || client === 'codex' ? 'mcp.copyCommand' : 'mcp.copyConfig')} />
    </div>
  );
}

function CreateTokenDialog({ kind, onClose }: { kind: Kind; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [name, setName] = useState('');
  const [lifetime, setLifetime] = useState('90');
  const [readOnly, setReadOnly] = useState(false);

  const create = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST('/api/v1/personal-token', {
        body: { name, kind, readOnly, expiresInDays: lifetime === 'never' ? null : Number(lifetime) },
      })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  });

  if (create.data) {
    return (
      <Dialog
        open
        title={t('tokens.createdTitle')}
        onClose={onClose}
        footer={<Button onClick={onClose}>{t('common.close')}</Button>}
      >
        <Typography role="alert" variant="body2" color="textSecondary">{t('tokens.createdHint')}</Typography>
        <CopyField label={t('tokens.token')} value={create.data.token ?? ''} copyLabel={t('tokens.copy')} copiedMessage={t('tokens.copied')} />
        {kind === MCP && (
          <>
            <Typography variant="body2" color="textSecondary">{t('mcp.createdConnect')}</Typography>
            <McpConnect token={create.data.token ?? ''} />
          </>
        )}
      </Dialog>
    );
  }

  return (
    <Dialog
      open
      title={t(kind === MCP ? 'tokens.createMcp' : 'tokens.createApi')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('tokens.create')}
          confirmDisabled={!name.trim() || create.isPending}
          onCancel={onClose}
          onConfirm={() => create.mutate()}
        />
      }
    >
      <TextField label={t('account.name')} autoFocus value={name} onChange={(event) => setName(event.target.value)} />
      <SelectField
        label={t('tokens.expires')}
        value={lifetime}
        onChange={(event) => setLifetime(event.target.value)}
        options={LIFETIMES.map((value) => ({ value, label: t(`tokens.lifetime.${value}`) }))}
      />
      <Checkbox label={t('tokens.readOnly')} checked={readOnly} onChange={(event) => setReadOnly(event.target.checked)} />
      {create.isError && <Typography role="alert" color="error" variant="body2">{toMessage(create.error)}</Typography>}
    </Dialog>
  );
}

function TokenSection({ kind, tokens, title, hint, docs, createLabel }: {
  kind: Kind;
  tokens: PersonalToken[];
  title: string;
  hint: string;
  docs: React.ReactNode;
  createLabel: string;
}) {
  const { t, i18n } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [creating, setCreating] = useState(false);
  const [revoking, setRevoking] = useState<PersonalToken | null>(null);

  const revoke = useMutation({
    mutationFn: (token: PersonalToken) => unwrap(apiClient.DELETE('/api/v1/personal-token/{id}', { params: { path: { id: token.id! } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
    onError: (failure) => toast('error', toMessage(failure)),
    onSettled: () => setRevoking(null),
  });

  return (
    <section className="flex flex-col gap-3">
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h2">{title}</Typography>
          <Typography variant="body2" color="textSecondary">{hint}</Typography>
        </div>
        {docs}
        <Button onClick={() => setCreating(true)}>{createLabel}</Button>
      </div>
      {tokens.length === 0 ? (
        <EmptyState title={t('tokens.empty')} />
      ) : (
        <Table
          label={title}
          rows={tokens}
          rowKey={(token) => token.id!}
          columns={[
            { key: 'name', header: t('account.name'), render: (token) => token.name },
            { key: 'prefix', header: t('tokens.token'), render: (token) => `${token.prefix}…` },
            {
              key: 'access',
              header: t('tokens.access'),
              render: (token) => <Badge tone={token.readOnly ? 'neutral' : 'info'}>{t(token.readOnly ? 'tokens.accessRead' : 'tokens.accessWrite')}</Badge>,
            },
            { key: 'used', header: t('tokens.lastUsed'), render: (token) => (token.lastUsedAt ? formatRelative(token.lastUsedAt, i18n.language) : t('tokens.neverUsed')) },
            { key: 'expires', header: t('tokens.expires'), render: (token) => (token.expiresAt ? formatDate(token.expiresAt, i18n.language) : t('tokens.lifetime.never')) },
            {
              key: 'revoke',
              header: <span className="sr-only">{t('tokens.revoke')}</span>,
              render: (token) => (
                <Link component="button" variant="body2" aria-label={`${t('tokens.revoke')} ${token.name}`} onClick={() => setRevoking(token)}>
                  {t('tokens.revoke')}
                </Link>
              ),
            },
          ]}
        />
      )}
      {creating && <CreateTokenDialog kind={kind} onClose={() => setCreating(false)} />}
      <DialogConfirm
        open={revoking !== null}
        title={t('tokens.revokeTitle', { name: revoking?.name })}
        description={t('tokens.revokeBody')}
        confirmLabel={t('tokens.revoke')}
        onCancel={() => setRevoking(null)}
        onConfirm={() => revoking && revoke.mutate(revoking)}
      />
    </section>
  );
}

export function TokensPage() {
  const { t } = useTranslation();
  const tokens = useQuery({ queryKey: KEY, queryFn: () => unwrap(apiClient.GET('/api/v1/personal-token')) });

  if (tokens.isPending) {
    return <LoadingState />;
  }
  if (tokens.isError) {
    return <ErrorState onRetry={() => void tokens.refetch()} />;
  }

  const ofKind = (kind: Kind) => tokens.data.filter((token) => token.kind === kind);

  return (
    <div className="flex flex-col gap-8">
      <Typography variant="h1">{t('tokens.title')}</Typography>
      <TokenSection
        kind={API}
        tokens={ofKind(API)}
        title={t('tokens.apiTitle')}
        hint={t('tokens.apiHint')}
        createLabel={t('tokens.createApi')}
        docs={<Button kind="secondary" component="a" href="/swagger" {...{ target: '_blank', rel: 'noreferrer' }}>{t('tokens.apiDocs')}</Button>}
      />
      <TokenSection
        kind={MCP}
        tokens={ofKind(MCP)}
        title={t('tokens.mcpTitle')}
        hint={t('tokens.mcpHint')}
        createLabel={t('tokens.createMcp')}
        docs={<Button kind="secondary" component={RouterLink} to="/account/mcp-docs">{t('tokens.mcpDocs')}</Button>}
      />
    </div>
  );
}

const MCP_TOOLS: { name: string; write: boolean }[] = [
  { name: 'list_spaces', write: false },
  { name: 'search_issues', write: false },
  { name: 'get_issue', write: false },
  { name: 'list_transitions', write: false },
  { name: 'create_issue', write: true },
  { name: 'update_issue', write: true },
  { name: 'transition_issue', write: true },
  { name: 'add_comment', write: true },
];

function CodeBlock({ code, copyLabel }: { code: string; copyLabel?: string }) {
  const { t } = useTranslation();
  const toast = useToast();

  const copy = async () => {
    await navigator.clipboard.writeText(code);
    toast('success', t('mcp.copied'));
  };

  return (
    <div className="rich-text relative">
      {/* Room on the right, so the copy button never covers the end of a line. */}
      <pre style={copyLabel ? { paddingRight: 44 } : undefined}><code>{code}</code></pre>
      {copyLabel && (
        <Tooltip title={copyLabel}>
          <IconButton
            size="small"
            aria-label={copyLabel}
            onClick={() => void copy()}
            sx={{
              position: 'absolute',
              top: 6,
              right: 6,
              backgroundColor: sem('bg-surface'),
              border: `1px solid ${sem('border-default')}`,
              borderRadius: `${radius.control}px`,
              color: sem('text-secondary'),
              '&:hover': { backgroundColor: sem('bg-hover'), color: sem('text-primary') },
            }}
          >
            <Copy size={16} strokeWidth={1.5} aria-hidden />
          </IconButton>
        </Tooltip>
      )}
    </div>
  );
}

export function McpDocsPage() {
  const { t } = useTranslation();
  const { url } = mcpSetup(MCP_PLACEHOLDER);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <Link component={RouterLink} to="/account/tokens" variant="body2">{t('mcp.back')}</Link>
        <Typography variant="h1">{t('mcp.title')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('mcp.intro')}</Typography>
      </div>
      <section className="flex flex-col gap-2">
        <Typography variant="h2">{t('mcp.connectTitle')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('mcp.connectHint')}</Typography>
        <CopyField label={t('mcp.url')} value={url} copyLabel={t('mcp.copyUrl')} copiedMessage={t('mcp.urlCopied')} />
        <McpConnect token={MCP_PLACEHOLDER} />
      </section>
      <section className="flex flex-col gap-2">
        <Typography variant="h2">{t('mcp.toolsTitle')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('mcp.toolsHint')}</Typography>
        <Table
          label={t('mcp.toolsTitle')}
          rows={MCP_TOOLS}
          rowKey={(tool) => tool.name}
          columns={[
            { key: 'name', header: t('mcp.tool'), render: (tool) => <code>{tool.name}</code> },
            { key: 'purpose', header: t('mcp.purpose'), render: (tool) => t(`mcp.tools.${tool.name}`) },
            {
              key: 'access',
              header: t('tokens.access'),
              render: (tool) => <Badge tone={tool.write ? 'info' : 'neutral'}>{t(tool.write ? 'tokens.accessWrite' : 'tokens.accessRead')}</Badge>,
            },
          ]}
        />
      </section>
      <section className="flex flex-col gap-2">
        <Typography variant="h2">{t('mcp.securityTitle')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('mcp.securityBody')}</Typography>
      </section>
    </div>
  );
}
