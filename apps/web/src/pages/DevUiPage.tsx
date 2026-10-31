import MenuList from '@mui/material/MenuList';
import Typography from '@mui/material/Typography';
import { Copy, Trash2 } from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Avatar } from '../components/Avatar';
import { Badge, StatusBadge } from '../components/Badge';
import { Button } from '../components/Button';
import { Checkbox, Toggle } from '../components/Checkbox';
import { Dialog, DialogConfirm, DialogFooter } from '../components/Dialog';
import { IssueTypeIcon, PriorityIcon } from '../components/IssueIcons';
import { MenuItem } from '../components/Menu';
import { Popover } from '../components/Popover';
import { SearchInput } from '../components/SearchInput';
import { Table } from '../components/Table';
import { Tabs } from '../components/Tabs';
import { SelectField, TextField } from '../components/TextField';
import { Toast } from '../components/Toast';

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3">
      <Typography variant="h2">{title}</Typography>
      {children}
    </section>
  );
}

export function DevUiPage() {
  const { t } = useTranslation();
  const [tab, setTab] = useState('all');
  const [dialogOpen, setDialogOpen] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [popoverAnchor, setPopoverAnchor] = useState<HTMLElement | null>(null);

  return (
    <div className="flex max-w-3xl flex-col gap-8">
      <Typography variant="h1">{t('devUi.title')}</Typography>

      <Section title={t('devUi.buttons')}>
        <div className="flex flex-wrap items-center gap-2">
          <Button>{t('devUi.primary')}</Button>
          <Button kind="secondary">{t('devUi.secondary')}</Button>
          <Button kind="ghost">{t('devUi.ghost')}</Button>
          <Button kind="danger">{t('devUi.danger')}</Button>
          <Button disabled>{t('devUi.disabled')}</Button>
        </div>
      </Section>

      <Section title={t('devUi.fields')}>
        <div className="grid grid-cols-2 gap-4">
          <TextField label={t('devUi.name')} defaultValue="Nguyễn Thị Ánh" />
          <TextField label={t('devUi.email')} error={t('devUi.nameRequired')} />
          <TextField label={t('devUi.disabled')} disabled />
          <SelectField
            label={t('devUi.type')}
            defaultValue="scrum"
            options={[
              { value: 'scrum', label: t('devUi.scrum') },
              { value: 'kanban', label: t('devUi.kanban') },
            ]}
          />
        </div>
        <SearchInput label={t('devUi.search')} />
      </Section>

      <Section title={t('devUi.selection')}>
        <div className="flex items-center gap-4">
          <Checkbox label={t('devUi.subscribe')} defaultChecked />
          <Checkbox label={t('devUi.primary')} />
          <Toggle label={t('devUi.enabled')} defaultChecked />
          <Toggle label={t('devUi.disabled')} />
        </div>
      </Section>

      <Section title={t('devUi.tabs')}>
        <Tabs
          label={t('devUi.tabs')}
          value={tab}
          onChange={setTab}
          items={[
            { value: 'all', label: t('devUi.tabAll') },
            { value: 'comments', label: t('devUi.tabComments') },
            { value: 'history', label: t('devUi.tabHistory') },
          ]}
        />
      </Section>

      <Section title={t('devUi.badges')}>
        <div className="flex flex-wrap items-center gap-2">
          <StatusBadge status="todo" />
          <StatusBadge status="inProgress" />
          <StatusBadge status="inReview" />
          <StatusBadge status="done" />
          <Badge tone="warning">{t('devUi.disabled')}</Badge>
          <Badge tone="danger">{t('devUi.danger')}</Badge>
          <IssueTypeIcon type="story" />
          <IssueTypeIcon type="task" />
          <IssueTypeIcon type="bug" />
          <IssueTypeIcon type="subtask" />
          <IssueTypeIcon type="epic" />
          <PriorityIcon priority="high" />
          <PriorityIcon priority="medium" />
          <PriorityIcon priority="low" />
          <Avatar name={t('devUi.assignee')} />
        </div>
      </Section>

      <Section title={t('devUi.menu')}>
        <MenuList sx={{ width: 200 }}>
          <MenuItem icon={Copy}>{t('devUi.copyLink')}</MenuItem>
          <MenuItem icon={Trash2} danger>
            {t('devUi.delete')}
          </MenuItem>
        </MenuList>
      </Section>

      <Section title={t('devUi.toast')}>
        <Toast kind="success" message={t('devUi.toastSuccess')} action={<Button kind="ghost">{t('common.undo')}</Button>} />
        <Toast kind="error" message={t('devUi.toastError')} action={<Button kind="ghost">{t('common.retry')}</Button>} />
        <Toast kind="info" message={t('devUi.toastInfo')} action={<Button kind="ghost">{t('devUi.toastOpen')}</Button>} />
      </Section>

      <Section title={t('devUi.table')}>
        <Table
          label={t('devUi.table')}
          rowKey={(row) => row.key}
          rows={[{ key: 'SB-12', title: t('devUi.rowTitle') }]}
          columns={[
            { key: 'key', header: t('devUi.columnKey'), render: (row) => row.key },
            { key: 'title', header: t('devUi.columnTitle'), render: (row) => row.title },
            { key: 'status', header: t('devUi.columnStatus'), render: () => <StatusBadge status="inProgress" /> },
          ]}
        />
      </Section>

      <Section title={t('devUi.overlays')}>
        <div className="flex gap-2">
          <Button kind="secondary" onClick={() => setDialogOpen(true)}>{t('devUi.openDialog')}</Button>
          <Button kind="secondary" onClick={() => setConfirmOpen(true)}>{t('devUi.openConfirm')}</Button>
          <Button kind="secondary" onClick={(event) => setPopoverAnchor(event.currentTarget)}>
            {t('devUi.openPopover')}
          </Button>
        </div>
        <Dialog
          open={dialogOpen}
          title={t('devUi.dialogTitle')}
          onClose={() => setDialogOpen(false)}
          footer={
            <DialogFooter
              confirmLabel={t('common.confirm')}
              onCancel={() => setDialogOpen(false)}
              onConfirm={() => setDialogOpen(false)}
            />
          }
        >
          <TextField label={t('devUi.name')} />
          <Typography color="textSecondary">{t('devUi.dialogBody')}</Typography>
        </Dialog>
        <DialogConfirm
          open={confirmOpen}
          title={t('devUi.confirmTitle')}
          description={t('devUi.confirmBody')}
          confirmLabel={t('devUi.confirmAction')}
          onCancel={() => setConfirmOpen(false)}
          onConfirm={() => setConfirmOpen(false)}
        />
        <Popover open={Boolean(popoverAnchor)} anchorEl={popoverAnchor} onClose={() => setPopoverAnchor(null)}>
          <Typography>{t('devUi.popoverBody')}</Typography>
        </Popover>
      </Section>
    </div>
  );
}
