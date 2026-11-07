import Typography from '@mui/material/Typography';
import { FileText } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useParams, useSearchParams } from 'react-router';
import { Badge } from '../../components/Badge';
import { LoadingState } from '../../components/StateViews';
import { Tabs } from '../../components/Tabs';
import { radius, sem } from '../../theme/tokens';
import { useSpace } from '../spaces/api';
import { IntroductionTab } from './IntroductionTab';
import { SummaryTab } from './SummaryTab';
import { TimelineTab } from './TimelineTab';

const TABS = ['summary', 'introduction', 'docs', 'timeline'] as const;
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
          {tab === 'docs' && <DocsTab />}
          {tab === 'timeline' && <TimelineTab space={space.data} />}
        </>
      )}
    </div>
  );
}

function DocsTab() {
  const { t } = useTranslation();

  return (
    <section className="flex min-h-[50vh] flex-col items-center justify-center gap-2 px-4 text-center">
      <span aria-hidden className="mb-2 flex size-12 items-center justify-center" style={{ backgroundColor: sem('bg-subtle'), borderRadius: radius.card, color: sem('text-secondary') }}>
        <FileText size={24} strokeWidth={1.5} />
      </span>
      <Badge tone="info">{t('overview.comingSoon')}</Badge>
      <Typography variant="h2">{t('overview.docsTitle')}</Typography>
      <Typography color="textSecondary" className="max-w-sm">{t('overview.docsDescription')}</Typography>
    </section>
  );
}
