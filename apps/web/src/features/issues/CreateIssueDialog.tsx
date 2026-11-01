import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import { Checkbox } from '../../components/Checkbox';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { UserPicker, type PickerUser } from '../../components/UserPicker';
import { useErrorMessage } from '../../lib/errors';
import { useMembers, useSpaces } from '../spaces/api';
import { usePriorities, useIssueTypes } from './api';
import { RichTextEditor } from './RichTextEditor';

interface CreateIssueDialogProps {
  onClose: () => void;
  /** Set when adding a subtask from an issue page. */
  parent?: { id: string; projectKey: string };
}

export function CreateIssueDialog({ onClose, parent }: CreateIssueDialogProps) {
  const { t } = useTranslation();
  const toMessage = useErrorMessage();
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { spaceKey } = useParams();
  const spaces = useSpaces();
  const writable = (spaces.data ?? []).filter((space) => space.permissions?.includes('Issue.Edit'));

  const [projectId, setProjectId] = useState('');
  const [typeId, setTypeId] = useState('');
  const [title, setTitle] = useState('');
  const [titleTouched, setTitleTouched] = useState(false);
  const [description, setDescription] = useState('');
  const [editorKey, setEditorKey] = useState(0);
  const [priorityId, setPriorityId] = useState('');
  const [assignee, setAssignee] = useState<PickerUser | null>(null);
  const [startDate, setStartDate] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [another, setAnother] = useState(false);

  const members = useMembers(projectId || undefined);
  const types = useIssueTypes(projectId || undefined);
  const priorities = usePriorities();
  const allowedTypes = (types.data ?? []).filter((type) => (parent ? type.hierarchy === 0 : type.hierarchy !== 0));
  const people = (members.data ?? []).map((member) => member.user!);
  const titleError = titleTouched && !title.trim() ? t('errors.IssueTitleRequired') : undefined;
  const dateError = startDate && dueDate && dueDate < startDate ? t('errors.DueDateBeforeStartDate') : undefined;

  useEffect(() => {
    if (!projectId && writable.length > 0) {
      const preferred = parent?.projectKey ?? spaceKey;
      setProjectId((writable.find((space) => space.key === preferred) ?? writable[0]!).id!);
    }
  }, [projectId, writable, parent, spaceKey]);

  useEffect(() => {
    if (allowedTypes.length > 0 && !allowedTypes.some((type) => type.id === typeId)) {
      setTypeId(allowedTypes.find((type) => type.name === 'Task')?.id ?? allowedTypes[0]!.id!);
    }
  }, [allowedTypes, typeId]);

  const create = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.POST('/api/v1/issue', {
          body: {
            projectId,
            typeId,
            title,
            description: description || null,
            priorityId: priorityId || null,
            assigneeId: assignee?.id ?? null,
            parentId: parent?.id ?? null,
            startDate: startDate || null,
            dueDate: dueDate || null,
          },
        }),
      ),
    onSuccess: async (issue) => {
      await queryClient.invalidateQueries({ queryKey: ['issues'] });
      toast('info', t('issues.created', { key: issue.key }));
      if (another) {
        setTitle('');
        setTitleTouched(false);
        setDescription('');
        setEditorKey((value) => value + 1);
      } else {
        onClose();
        if (!parent) {
          await navigate(`/issues/${issue.key}`);
        }
      }
    },
  });

  const submit = () => {
    setTitleTouched(true);
    if (title.trim() && !dateError) {
      create.mutate();
    }
  };

  return (
    <Dialog
      open
      width={640}
      title={t(parent ? 'issues.addSubtask' : 'issues.create')}
      onClose={onClose}
      footer={
        <div className="flex items-center justify-between">
          <Checkbox label={t('issues.createAnother')} checked={another} onChange={(event) => setAnother(event.target.checked)} />
          <DialogFooter
            confirmLabel={t('issues.createAction')}
            confirmDisabled={create.isPending || !projectId || !typeId}
            onCancel={onClose}
            onConfirm={submit}
          />
        </div>
      }
    >
      <div className="grid grid-cols-2 gap-4">
        <SelectField
          label={t('issues.space')}
          value={projectId}
          disabled={Boolean(parent)}
          onChange={(event) => setProjectId(event.target.value)}
          options={writable.map((space) => ({ value: space.id!, label: space.name! }))}
        />
        <SelectField
          label={t('issues.issueType')}
          value={typeId}
          onChange={(event) => setTypeId(event.target.value)}
          options={allowedTypes.map((type) => ({ value: type.id!, label: t(`issue.type.${type.icon}`) }))}
        />
      </div>
      <TextField label={t('issues.titleField')} autoFocus value={title} error={titleError} onBlur={() => setTitleTouched(true)} onChange={(event) => setTitle(event.target.value)} />
      <RichTextEditor key={editorKey} label={t('issues.description')} people={people} onChange={setDescription} />
      <div className="grid grid-cols-2 gap-4">
        <UserPicker label={t('issues.assignee')} options={people} value={assignee} onChange={setAssignee} />
        <SelectField
          label={t('issues.priority')}
          value={priorityId || (priorities.data?.find((priority) => priority.name === 'Medium')?.id ?? '')}
          onChange={(event) => setPriorityId(event.target.value)}
          options={(priorities.data ?? []).map((priority) => ({ value: priority.id!, label: priority.name! }))}
        />
        <TextField label={t('issues.startDate')} type="date" value={startDate} onChange={(event) => setStartDate(event.target.value)} />
        <TextField label={t('issues.dueDate')} type="date" value={dueDate} error={dateError} onChange={(event) => setDueDate(event.target.value)} />
      </div>
      {create.isError && <Typography role="alert" color="error" variant="body2">{toMessage(create.error)}</Typography>}
    </Dialog>
  );
}
