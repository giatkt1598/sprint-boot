import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type ChangeEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink, useNavigate, useParams } from 'react-router';
import type { components } from '@sprint-boot/api-client';
import { apiClient, unwrap } from '../../api';
import { Badge } from '../../components/Badge';
import { Button } from '../../components/Button';
import { CopyField } from '../../components/CopyField';
import { CustomSignInButton } from '../../components/CustomSignInButton';
import { Toggle } from '../../components/Checkbox';
import { Dialog, DialogConfirm, DialogFooter } from '../../components/Dialog';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { useErrorMessage } from '../../lib/errors';
import { NotFoundPage } from '../../pages/NotFoundPage';
import { radius, sem } from '../../theme/tokens';

type Provider = components['schemas']['ExternalProviderDto'];
type SaveProvider = components['schemas']['SaveProviderInput'];

const OIDC = 0;
const GOOGLE = 1;
const GITHUB = 2;
const SAML = 3;
const LDAP = 4;
const DISPLAY_NAMES: Record<number, string> = { [GOOGLE]: 'Google', [GITHUB]: 'GitHub' };
const TYPE_LABELS: Record<number, string> = { [OIDC]: 'OIDC', [GOOGLE]: 'OAuth', [GITHUB]: 'OAuth', [SAML]: 'SAML 2.0', [LDAP]: 'LDAP' };

/** Every protocol keeps its address, its identifier and its secret in the same three fields, under its own names. */
const FIELD_LABELS: Record<number, { address: string; id: string; secret: string | null }> = {
  [OIDC]: { address: 'sso.discoveryUrl', id: 'sso.clientId', secret: 'sso.clientSecret' },
  [GOOGLE]: { address: 'sso.discoveryUrl', id: 'sso.clientId', secret: 'sso.clientSecret' },
  [GITHUB]: { address: 'sso.discoveryUrl', id: 'sso.clientId', secret: 'sso.clientSecret' },
  [SAML]: { address: 'sso.samlMetadataUrl', id: 'sso.samlEntityId', secret: null },
  [LDAP]: { address: 'sso.ldapServer', id: 'sso.ldapBindDn', secret: 'sso.ldapBindPassword' },
};

/** Types whose address the admin types in; Google and GitHub have a fixed one. */
const hasAddress = (type: number | undefined) => type === OIDC || type === SAML || type === LDAP;

const DEFAULT_MAPPING: Record<number, { email: string; name: string }> = {
  [SAML]: { email: 'email', name: 'name' },
  [LDAP]: { email: 'mail', name: 'displayName' },
};

const samlMetadataUrl = (name: string) => `${window.location.origin}/connect/signin-${name}`;

export const toProviderName = (displayName: string) =>
  displayName.toLowerCase().normalize('NFD').replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 32);

const useProviders = () =>
  useQuery({ queryKey: ['admin', 'sso', 'providers'], queryFn: () => unwrap(apiClient.GET('/api/v1/sso-admin/providers')) });

const toInput = (provider: Provider): SaveProvider => ({
  name: provider.name!,
  displayName: provider.displayName!,
  type: provider.type,
  discoveryUrl: provider.discoveryUrl,
  clientId: provider.clientId!,
  clientSecret: '',
  scopes: provider.scopes,
  emailClaim: provider.emailClaim,
  nameClaim: provider.nameClaim,
  isEnabled: provider.isEnabled,
  buttonColor: provider.buttonColor,
  searchBase: provider.searchBase,
  userFilter: provider.userFilter,
});

const HEX_COLOR = /^#[0-9A-Fa-f]{6}$/;
const DEFAULT_PICKER_COLOR = '#2563EB';

/** Logo uploads at once; the color is saved with the rest of the form. The preview shows both. */
function SignInButtonLook({ provider, displayName, color, onColor }: {
  provider: Provider;
  displayName: string;
  color: string;
  onColor: (color: string) => void;
}) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['admin', 'sso'] });

  const upload = useMutation({
    mutationFn: (file: File) =>
      unwrap(
        apiClient.POST('/api/v1/sso-admin/{id}/upload-logo', {
          params: { path: { id: provider.id! } },
          body: {} as never,
          bodySerializer: () => {
            const form = new FormData();
            form.append('file', file);
            return form;
          },
        }),
      ),
    onSuccess: refresh,
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/sso-admin/{id}/logo', { params: { path: { id: provider.id! } } })),
    onSuccess: refresh,
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const pick = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) {
      upload.mutate(file);
    }
  };
  const valid = color === '' || HEX_COLOR.test(color);

  return (
    <section className="flex flex-col gap-4 p-4" style={{ maxWidth: 800, border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}>
      <div>
        <Typography variant="h2">{t('sso.buttonLook')}</Typography>
        <Typography variant="body2" color="textSecondary">{t('sso.buttonLookHint')}</Typography>
      </div>
      <div className="flex flex-col gap-1">
        <Typography variant="caption" color="textSecondary">{t('sso.logo')}</Typography>
        <div className="flex items-center gap-3">
          {provider.logoUrl && <img src={provider.logoUrl} alt="" width={32} height={32} style={{ objectFit: 'contain' }} />}
          <Button kind="secondary" disabled={upload.isPending} onClick={() => input.current?.click()}>
            {t(provider.logoUrl ? 'sso.changeLogo' : 'sso.uploadLogo')}
          </Button>
          {provider.logoUrl && <Button kind="ghost" disabled={remove.isPending} onClick={() => remove.mutate()}>{t('sso.removeLogo')}</Button>}
        </div>
        <Typography variant="caption" color="textSecondary">{t('sso.logoHint')}</Typography>
        <input ref={input} type="file" accept="image/png,image/jpeg,image/webp,image/gif" hidden aria-label={t('sso.uploadLogo')} onChange={pick} />
      </div>
      <div className="flex items-end gap-3">
        <input
          type="color"
          aria-label={t('sso.buttonColor')}
          value={HEX_COLOR.test(color) ? color : DEFAULT_PICKER_COLOR}
          onChange={(event) => onColor(event.target.value.toUpperCase())}
          style={{ width: 40, height: 36, padding: 0, border: `1px solid ${sem('border-strong')}`, borderRadius: radius.control, background: 'none' }}
        />
        <div className="w-40">
          <TextField
            label={t('sso.buttonColor')}
            placeholder={t('sso.defaultColor')}
            value={color}
            error={valid ? undefined : t('sso.colorInvalid')}
            onChange={(event) => onColor(event.target.value.trim())}
          />
        </div>
        {color && <Button kind="ghost" onClick={() => onColor('')}>{t('sso.resetColor')}</Button>}
      </div>
      <div className="flex flex-col gap-2" style={{ maxWidth: 400 }}>
        <Typography variant="caption" color="textSecondary">{t('sso.preview')}</Typography>
        <div aria-hidden className="p-4" style={{ backgroundColor: sem('bg-page'), borderRadius: radius.card }}>
          <CustomSignInButton
            label={t('auth.continueWith', { provider: displayName || provider.displayName })}
            logoUrl={provider.logoUrl}
            color={valid && color ? color : null}
          />
        </div>
      </div>
    </section>
  );
}

function useSaveProvider(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SaveProvider) =>
      unwrap(apiClient.PUT('/api/v1/sso-admin/{id}/provider', { params: { path: { id } }, body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'sso'] }),
  });
}

function PolicyCard() {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const policy = useQuery({ queryKey: ['admin', 'sso', 'policy'], queryFn: () => unwrap(apiClient.GET('/api/v1/sso-admin/policy')) });
  const [domains, setDomains] = useState('');

  useEffect(() => setDomains(policy.data?.allowedDomains ?? ''), [policy.data?.allowedDomains]);

  const save = useMutation({
    mutationFn: (body: components['schemas']['SaveAuthPolicyInput']) => unwrap(apiClient.PUT('/api/v1/sso-admin/policy', { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'sso', 'policy'] }),
    onError: (failure) => toast('error', toMessage(failure)),
  });

  if (policy.isPending) {
    return <LoadingState />;
  }
  if (policy.isError) {
    return <ErrorState onRetry={() => void policy.refetch()} />;
  }

  const current = {
    localLoginEnabled: policy.data.localLoginEnabled,
    selfRegistrationEnabled: policy.data.selfRegistrationEnabled,
    allowedDomains: policy.data.allowedDomains,
  };

  return (
    <section
      className="flex flex-col gap-4 p-4"
      style={{ maxWidth: 800, border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}
    >
      <Typography variant="h2">{t('sso.policy')}</Typography>
      <div className="flex items-center gap-4">
        <div className="flex-1">
          <Typography>{t('sso.local')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('sso.localHint')}</Typography>
        </div>
        <Toggle label={t('sso.local')} checked={current.localLoginEnabled ?? true} onChange={(event) => save.mutate({ ...current, localLoginEnabled: event.target.checked })} />
      </div>
      <div className="flex items-center gap-4">
        <div className="flex-1">
          <Typography>{t('sso.selfSignUp')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('sso.selfSignUpHint')}</Typography>
        </div>
        <Toggle label={t('sso.selfSignUp')} checked={current.selfRegistrationEnabled ?? false} onChange={(event) => save.mutate({ ...current, selfRegistrationEnabled: event.target.checked })} />
      </div>
      <TextField
        label={t('sso.domains')}
        value={domains}
        onChange={(event) => setDomains(event.target.value)}
        onBlur={() => domains !== (current.allowedDomains ?? '') && save.mutate({ ...current, allowedDomains: domains })}
      />
      <Typography variant="body2" color="textSecondary">{t('sso.domainsHint')}</Typography>
    </section>
  );
}

/** Sign-in goes through this same address, so it is what the provider must call back. */
function RedirectUrlField({ name, type = OIDC }: { name: string; type?: number }) {
  const { t } = useTranslation();
  if (type === LDAP) {
    return null;
  }
  if (type === SAML) {
    // The IdP reads our metadata from the first address and posts its answer to the second.
    return (
      <>
        <CopyField label={t('sso.samlSpMetadata')} value={samlMetadataUrl(name)} copyLabel={t('sso.copyRedirect')} copiedMessage={t('sso.redirectCopied')} />
        <CopyField label={t('sso.samlAcs')} value={`${samlMetadataUrl(name)}/Acs`} copyLabel={t('sso.copyRedirect')} copiedMessage={t('sso.redirectCopied')} />
      </>
    );
  }
  return (
    <CopyField
      label={t('sso.redirectUrl')}
      value={`${window.location.origin}/connect/signin-${name}`}
      copyLabel={t('sso.copyRedirect')}
      copiedMessage={t('sso.redirectCopied')}
    />
  );
}

function LdapFields({ searchBase, userFilter, onSearchBase, onUserFilter }: {
  searchBase: string;
  userFilter: string;
  onSearchBase: (value: string) => void;
  onUserFilter: (value: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <>
      <TextField label={t('sso.ldapSearchBase')} placeholder="ou=people,dc=example,dc=com" value={searchBase} onChange={(event) => onSearchBase(event.target.value)} />
      <TextField label={t('sso.ldapUserFilter')} placeholder="(|(uid={0})(sAMAccountName={0})(mail={0}))" value={userFilter} onChange={(event) => onUserFilter(event.target.value)} />
      <Typography variant="body2" color="textSecondary">{t('sso.ldapUserFilterHint')}</Typography>
    </>
  );
}

function AddProviderDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const queryClient = useQueryClient();
  const [type, setType] = useState<SaveProvider['type']>(OIDC);
  const [displayName, setDisplayName] = useState('');
  const [discoveryUrl, setDiscoveryUrl] = useState('');
  const [clientId, setClientId] = useState('');
  const [clientSecret, setClientSecret] = useState('');
  const [searchBase, setSearchBase] = useState('');
  const [userFilter, setUserFilter] = useState('');
  const shownName = DISPLAY_NAMES[type ?? OIDC] ?? displayName;
  const labels = FIELD_LABELS[type ?? OIDC]!;
  const name = toProviderName(shownName);
  // The SP entity ID only has to be unique; our metadata address is the usual choice.
  const entityId = type === SAML && !clientId ? samlMetadataUrl(name) : clientId;
  const needsSecret = type === OIDC || type === GOOGLE || type === GITHUB;

  const add = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.POST('/api/v1/sso-admin/provider', {
          body: {
            name,
            displayName: shownName,
            type,
            discoveryUrl: hasAddress(type) ? discoveryUrl : null,
            clientId: entityId,
            clientSecret,
            emailClaim: DEFAULT_MAPPING[type ?? OIDC]?.email,
            nameClaim: DEFAULT_MAPPING[type ?? OIDC]?.name,
            searchBase: type === LDAP ? searchBase : null,
            userFilter: type === LDAP ? userFilter || null : null,
            isEnabled: true,
          },
        }),
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'sso'] });
      onClose();
    },
  });

  return (
    <Dialog
      open
      title={t('sso.addTitle')}
      onClose={onClose}
      footer={
        <DialogFooter
          confirmLabel={t('sso.add')}
          confirmDisabled={!name || !entityId || (needsSecret && !clientSecret) || (hasAddress(type) && !discoveryUrl) || (type === LDAP && !searchBase) || add.isPending}
          onCancel={onClose}
          onConfirm={() => add.mutate()}
        />
      }
    >
      <SelectField
        label={t('sso.type')}
        value={String(type)}
        onChange={(event) => setType(Number(event.target.value) as SaveProvider['type'])}
        options={[
          { value: String(OIDC), label: t('sso.oidc') },
          { value: String(GOOGLE), label: 'Google' },
          { value: String(GITHUB), label: 'GitHub' },
          { value: String(SAML), label: 'SAML 2.0' },
          { value: String(LDAP), label: t('sso.ldap') },
        ]}
      />
      {hasAddress(type) && (
        <>
          <TextField label={t('sso.displayName')} autoFocus value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
          <TextField label={t(labels.address)} placeholder={type === LDAP ? 'ldaps://ldap.example.com:636' : undefined} value={discoveryUrl} onChange={(event) => setDiscoveryUrl(event.target.value)} />
        </>
      )}
      <RedirectUrlField name={name} type={type} />
      <TextField label={t(labels.id)} placeholder={type === SAML ? samlMetadataUrl(name) : undefined} value={clientId} onChange={(event) => setClientId(event.target.value)} />
      {labels.secret && (
        <TextField label={t(labels.secret)} type="password" autoComplete="off" placeholder={type === LDAP ? undefined : t('sso.secretPlaceholder')} value={clientSecret} onChange={(event) => setClientSecret(event.target.value)} />
      )}
      {type === LDAP && <LdapFields searchBase={searchBase} userFilter={userFilter} onSearchBase={setSearchBase} onUserFilter={setUserFilter} />}
      {add.isError && <Typography role="alert" color="error" variant="body2">{toMessage(add.error)}</Typography>}
    </Dialog>
  );
}

function ProviderToggle({ provider }: { provider: Provider }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const save = useSaveProvider(provider.id!);

  return (
    <Toggle
      label={t('sso.enable', { provider: provider.displayName })}
      checked={provider.isEnabled ?? false}
      onChange={(event) =>
        save.mutate({ ...toInput(provider), isEnabled: event.target.checked }, { onError: (failure) => toast('error', toMessage(failure)) })
      }
    />
  );
}

export function SsoPage() {
  const { t } = useTranslation();
  const providers = useProviders();
  const [adding, setAdding] = useState(false);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-start gap-2">
        <div className="flex-1">
          <Typography variant="h1">{t('sso.title')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('sso.subtitle')}</Typography>
        </div>
        <Button onClick={() => setAdding(true)}>{t('sso.addProvider')}</Button>
      </div>
      <PolicyCard />
      {providers.isPending && <LoadingState />}
      {providers.isError && <ErrorState onRetry={() => void providers.refetch()} />}
      {providers.data?.length === 0 && <EmptyState title={t('sso.empty')} />}
      {(providers.data?.length ?? 0) > 0 && (
        <Table<Provider>
          label={t('sso.title')}
          rows={providers.data ?? []}
          rowKey={(item) => item.id!}
          columns={[
            { key: 'name', header: t('sso.provider'), render: (item) => item.displayName },
            { key: 'type', header: t('sso.type'), render: (item) => TYPE_LABELS[item.type ?? OIDC] },
            {
              key: 'status',
              header: t('sso.status'),
              render: (item) =>
                item.hasSecret || item.type === SAML ? <Badge tone="success">{t('sso.connected')}</Badge> : <Badge>{t('sso.notSetUp')}</Badge>,
            },
            { key: 'enabled', header: t('sso.enabled'), render: (item) => <ProviderToggle provider={item} /> },
            {
              key: 'edit',
              header: <span className="sr-only">{t('sso.edit')}</span>,
              render: (item) => (
                <Link component={RouterLink} to={item.id!} variant="body2" aria-label={`${t('sso.edit')} ${item.displayName}`}>{t('sso.edit')}</Link>
              ),
            },
          ]}
        />
      )}
      {adding && <AddProviderDialog onClose={() => setAdding(false)} />}
    </div>
  );
}

const parseMapping = (text: string) =>
  Object.fromEntries(text.split(',').map((pair) => pair.split('=').map((part) => part.trim())));

function ProviderForm({ provider }: { provider: Provider }) {
  const { t } = useTranslation();
  const toast = useToast();
  const toMessage = useErrorMessage();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [displayName, setDisplayName] = useState(provider.displayName ?? '');
  const [discoveryUrl, setDiscoveryUrl] = useState(provider.discoveryUrl ?? '');
  const [clientId, setClientId] = useState(provider.clientId ?? '');
  const [clientSecret, setClientSecret] = useState('');
  const [scopes, setScopes] = useState(provider.scopes ?? 'openid profile email');
  const [mapping, setMapping] = useState(`email=${provider.emailClaim ?? 'email'}, name=${provider.nameClaim ?? 'name'}`);
  const [deleting, setDeleting] = useState(false);
  const [buttonColor, setButtonColor] = useState(provider.buttonColor ?? '');
  const [searchBase, setSearchBase] = useState(provider.searchBase ?? '');
  const [userFilter, setUserFilter] = useState(provider.userFilter ?? '');
  const isOidc = provider.type === OIDC;
  const type = provider.type ?? OIDC;
  const labels = FIELD_LABELS[type]!;
  const directory = type === SAML || type === LDAP;

  const save = useSaveProvider(provider.id!);
  const test = useMutation({
    mutationFn: () =>
      unwrap(apiClient.POST('/api/v1/sso-admin/test-connection', { body: { type, discoveryUrl, clientId, clientSecret: clientSecret || null, providerId: provider.id } })),
    onSuccess: (result) => toast(result.succeeded ? 'success' : 'error', t(result.succeeded ? 'sso.testPassed' : 'sso.testFailed')),
    onError: (failure) => toast('error', toMessage(failure)),
  });
  const remove = useMutation({
    mutationFn: () => unwrap(apiClient.DELETE('/api/v1/sso-admin/{id}/provider', { params: { path: { id: provider.id! } } })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['admin', 'sso'] });
      await navigate('..', { relative: 'path' });
    },
    onError: (failure) => toast('error', toMessage(failure)),
  });

  const submit = () => {
    const claims = parseMapping(mapping) as { email?: string; name?: string };
    save.mutate(
      {
        ...toInput(provider),
        displayName,
        discoveryUrl: hasAddress(type) ? discoveryUrl : null,
        clientId,
        clientSecret,
        scopes,
        searchBase: type === LDAP ? searchBase : null,
        userFilter: type === LDAP ? userFilter || null : null,
        emailClaim: claims.email,
        nameClaim: claims.name,
        buttonColor: isOidc && HEX_COLOR.test(buttonColor) ? buttonColor : null,
      },
      {
        onSuccess: () => {
          setClientSecret('');
          toast('success', t('account.saved'));
        },
        onError: (failure) => toast('error', toMessage(failure)),
      },
    );
  };

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Typography variant="h1">{provider.displayName}</Typography>
        <Typography variant="body2" color="textSecondary">
          {t(type === SAML ? 'sso.samlProvider' : type === LDAP ? 'sso.ldapProvider' : isOidc ? 'sso.oidcProvider' : 'sso.oauthProvider')}
        </Typography>
      </div>
      <section className="flex flex-col gap-4 p-4" style={{ maxWidth: 800, border: `1px solid ${sem('border-default')}`, borderRadius: radius.card }}>
        <Typography variant="h2">{t('sso.connection')}</Typography>
        <TextField label={t('sso.displayName')} value={displayName} onChange={(event) => setDisplayName(event.target.value)} />
        {hasAddress(type) && <TextField label={t(labels.address)} value={discoveryUrl} onChange={(event) => setDiscoveryUrl(event.target.value)} />}
        <RedirectUrlField name={provider.name!} type={type} />
        <TextField label={t(labels.id)} value={clientId} onChange={(event) => setClientId(event.target.value)} />
        {labels.secret && (
          <>
            <TextField label={t(labels.secret)} type="password" autoComplete="off" placeholder={provider.hasSecret ? '••••••••••••' : t('sso.secretPlaceholder')} value={clientSecret} onChange={(event) => setClientSecret(event.target.value)} />
            <Typography variant="body2" color="textSecondary">{t('sso.secretHint')}</Typography>
          </>
        )}
        {type === LDAP && <LdapFields searchBase={searchBase} userFilter={userFilter} onSearchBase={setSearchBase} onUserFilter={setUserFilter} />}
        {isOidc && <TextField label={t('sso.scopes')} value={scopes} onChange={(event) => setScopes(event.target.value)} />}
        {(isOidc || directory) && (
          <TextField label={t(directory ? 'sso.attributeMapping' : 'sso.claimMapping')} value={mapping} onChange={(event) => setMapping(event.target.value)} />
        )}
        {directory && <Typography variant="body2" color="textSecondary">{t('sso.directoryEmailTrusted')}</Typography>}
        <div className="flex justify-end gap-2">
          <Button kind="ghost" onClick={() => setDeleting(true)}>{t('sso.delete')}</Button>
          {(isOidc || directory) && <Button kind="secondary" disabled={test.isPending} onClick={() => test.mutate()}>{t('sso.test')}</Button>}
          <Button disabled={save.isPending || (buttonColor !== '' && !HEX_COLOR.test(buttonColor))} onClick={submit}>{t('sso.save')}</Button>
        </div>
      </section>
      {isOidc && <SignInButtonLook provider={provider} displayName={displayName} color={buttonColor} onColor={setButtonColor} />}
      <DialogConfirm
        open={deleting}
        title={t('sso.deleteTitle')}
        description={t('sso.deleteBody', { provider: provider.displayName })}
        confirmLabel={t('sso.delete')}
        onCancel={() => setDeleting(false)}
        onConfirm={() => remove.mutate()}
      />
    </div>
  );
}

export function SsoProviderPage() {
  const { providerId } = useParams();
  const providers = useProviders();

  if (providers.isPending) {
    return <LoadingState />;
  }
  if (providers.isError) {
    return <ErrorState onRetry={() => void providers.refetch()} />;
  }

  const provider = providers.data.find((item) => item.id === providerId);
  return provider ? <ProviderForm key={provider.id} provider={provider} /> : <NotFoundPage />;
}
