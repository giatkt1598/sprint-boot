import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Badge } from '../components/Badge';
import { apiClient } from '../api';

export function HomePage() {
  const { t } = useTranslation();
  const ping = useQuery({
    queryKey: ['system', 'ping'],
    queryFn: async () => {
      const { data, error } = await apiClient.GET('/api/v1/system/ping');
      if (!data) {
        throw new Error(String(error));
      }
      return data;
    },
    retry: false,
  });

  return (
    <div className="flex flex-col gap-4">
      <Typography variant="h1">{t('home.title')}</Typography>
      {ping.isPending && <Typography color="textSecondary">{t('home.apiLoading')}</Typography>}
      {ping.isError && <Typography role="alert" color="error">{t('home.apiError')}</Typography>}
      {ping.data && (
        <dl className="flex flex-col gap-2">
          <div className="flex items-center gap-2">
            <dt>{t('home.apiStatus')}</dt>
            <dd data-testid="api-status"><Badge tone="success">{ping.data.status}</Badge></dd>
          </div>
          <div className="flex items-center gap-2">
            <dt>{t('home.apiVersion')}</dt>
            <dd>{ping.data.version}</dd>
          </div>
        </dl>
      )}
    </div>
  );
}
