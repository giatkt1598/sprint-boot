import Typography from '@mui/material/Typography';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router';
import { Avatar } from '../../components/Avatar';
import { SearchInput } from '../../components/SearchInput';
import { EmptyState, ErrorState, LoadingState } from '../../components/StateViews';
import { Table } from '../../components/Table';
import { formatDate } from '../../lib/format';
import { sem } from '../../theme/tokens';
import { useMembers, useSpace, type SpaceMember } from '../spaces/api';
import { ROLE_LABELS } from '../spaces/SpaceSettings';

/** Everyone in the space, as read-only profiles; changing roles stays in the space settings. */
export function PeoplePage() {
  const { t, i18n } = useTranslation();
  const { spaceKey } = useParams();
  const space = useSpace(spaceKey);
  const members = useMembers(space.data?.id);
  const [filter, setFilter] = useState('');

  const needle = filter.trim().toLowerCase();
  const shown = (members.data ?? []).filter((member) =>
    [member.user?.name, member.user?.email, member.user?.title].some((value) => (value ?? '').toLowerCase().includes(needle)),
  );

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-2">
        <div className="flex-1">
          <Typography variant="h1">{t('people.title')}</Typography>
          <Typography variant="body2" color="textSecondary">{t('people.subtitle', { count: members.data?.length ?? 0 })}</Typography>
        </div>
        <SearchInput label={t('people.search')} width={240} value={filter} onChange={(event) => setFilter(event.target.value)} />
      </div>
      {(space.isPending || members.isPending) && !space.isError && <LoadingState />}
      {(space.isError || members.isError) && <ErrorState onRetry={() => void members.refetch()} />}
      {members.data && shown.length === 0 && <EmptyState title={t('people.none')} />}
      {members.data && shown.length > 0 && (
        <Table<SpaceMember>
          label={t('people.title')}
          rows={shown}
          rowKey={(member) => member.user!.id!}
          columns={[
            {
              key: 'name',
              header: t('people.person'),
              render: (member) => (
                <span className="flex items-center gap-2">
                  <Avatar name={member.user?.name ?? ''} src={member.user?.avatarUrl} />
                  {member.user?.name}
                </span>
              ),
            },
            { key: 'title', header: t('account.jobTitle'), render: (member) => member.user?.title || <span style={{ color: sem('text-tertiary') }}>—</span> },
            { key: 'email', header: t('auth.email'), render: (member) => member.user?.email },
            { key: 'role', header: t('spaces.role'), render: (member) => t(ROLE_LABELS[member.role ?? ''] ?? 'spaces.roleMember') },
            { key: 'joined', header: t('spaces.joined'), render: (member) => formatDate(member.joinedAt, i18n.language) },
          ]}
        />
      )}
    </div>
  );
}
