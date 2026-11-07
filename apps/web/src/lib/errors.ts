import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../api';

/** Turns a failure into a localized message, falling back to a generic one. */
export function useErrorMessage() {
  const { t, i18n } = useTranslation();

  return useCallback(
    (failure: unknown) => {
      if (failure instanceof ApiError) {
        const key = `errors.${failure.code?.split(':')[1]}`;
        if (failure.code && i18n.exists(key)) {
          return t(key);
        }
        if (failure.status === 400) {
          return t('errors.validation');
        }
        if (failure.status === 403) {
          return t('errors.forbidden');
        }
        if (failure.status === 409) {
          return t('errors.conflict');
        }
      }
      return t('errors.generic');
    },
    [t, i18n],
  );
}

/** Missing and forbidden both come back as not found, so the UI never tells whether something exists. */
export const isNotFound = (failure: unknown) => failure instanceof ApiError && (failure.status === 404 || failure.status === 403);
