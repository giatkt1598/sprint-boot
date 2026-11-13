import { useTranslation } from 'react-i18next';
import { Outlet, useParams } from 'react-router';
import { isNotFound } from '../../lib/errors';
import { NotFoundPage } from '../../pages/NotFoundPage';
import { useSpace } from './api';
import { ArchivedBanner } from './SpaceLifecycle';

/** Every page of a space first checks the space is there; the pages share the cached answer. */
export function SpaceGuard() {
  const { t } = useTranslation();
  const { spaceKey } = useParams();
  const space = useSpace(spaceKey);

  if (space.isError && isNotFound(space.error)) {
    return <NotFoundPage title={t('notFound.spaceTitle')} description={t('notFound.spaceDescription')} />;
  }
  return (
    <>
      {/* Every page of an archived space is read-only; the banner says so once, above whichever page is open. */}
      {space.data?.archivedAt && <div className="pb-4"><ArchivedBanner space={space.data} /></div>}
      <Outlet />
    </>
  );
}
