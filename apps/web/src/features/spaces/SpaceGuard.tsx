import { useTranslation } from 'react-i18next';
import { Outlet, useParams } from 'react-router';
import { isNotFound } from '../../lib/errors';
import { NotFoundPage } from '../../pages/NotFoundPage';
import { useSpace } from './api';

/** Every page of a space first checks the space is there; the pages share the cached answer. */
export function SpaceGuard() {
  const { t } = useTranslation();
  const { spaceKey } = useParams();
  const space = useSpace(spaceKey);

  if (space.isError && isNotFound(space.error)) {
    return <NotFoundPage title={t('notFound.spaceTitle')} description={t('notFound.spaceDescription')} />;
  }
  return <Outlet />;
}
