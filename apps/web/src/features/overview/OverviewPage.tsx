import Typography from '@mui/material/Typography';
import { useTranslation } from 'react-i18next';
import { useParams, useSearchParams } from 'react-router';
import { LoadingState } from '../../components/StateViews';
import { Tabs } from '../../components/Tabs';
import { sem } from '../../theme/tokens';
import { DocsTab } from '../docs/DocsViews';
import { useSpace } from '../spaces/api';
import { GoalsTab } from './GoalsTab';
import { IntroductionTab } from './IntroductionTab';
import { SummaryTab } from './SummaryTab';
import { TimelineTab } from './TimelineTab';

const TABS = ['summary', 'introduction', 'goals', 'docs', 'timeline'] as const;
type Tab = (typeof TABS)[number];

/** A space at a glance. Each tab has its own address, so a tab can be shared and reloaded. */
export function OverviewPage() {
  const { t } = useTranslation();
  const { spaceKey } = useParams();
  const space = useSpace(spaceKey);
  const [params, setParams] = useSearchParams();
  const requested = params.get('tab') as Tab | null;
  const tab: Tab = requested && TABS.includes(requested) ? requested : 'summary';

  return (
    <div className="flex flex-col gap-4">
      <Typography variant="h1">{t('overview.title')}</Typography>
      <div style={{ borderBottom: `1px solid ${sem('border-default')}` }}>
        <Tabs
          label={t('overview.tabs')}
          items={TABS.map((value) => ({ value, label: t(`overview.tab.${value}`) }))}
          value={tab}
          onChange={(next) => setParams(next === 'summary' ? {} : { tab: next }, { replace: true })}
        />
      </div>
      {!space.data ? (
        <LoadingState />
      ) : (
        <>
          {tab === 'summary' && <SummaryTab space={space.data} />}
          {tab === 'introduction' && <IntroductionTab space={space.data} />}
          {tab === 'goals' && <GoalsTab space={space.data} />}
          {tab === 'docs' && <DocsTab space={space.data} />}
          {tab === 'timeline' && <TimelineTab space={space.data} />}
        </>
      )}
    </div>
  );
}
