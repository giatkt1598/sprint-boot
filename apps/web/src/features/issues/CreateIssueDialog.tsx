import Typography from '@mui/material/Typography';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type ChangeEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router';
import { apiClient, unwrap } from '../../api';
import IconButton from '@mui/material/IconButton';
import { Paperclip, X } from 'lucide-react';
import { Button } from '../../components/Button';
import { Checkbox } from '../../components/Checkbox';
import { DatePicker } from '../../components/DatePicker';
import { Dialog, DialogFooter } from '../../components/Dialog';
import { SelectField, TextField } from '../../components/TextField';
import { useToast } from '../../components/ToastHost';
import { UserPicker, type PickerUser } from '../../components/UserPicker';
import { useErrorMessage } from '../../lib/errors';
import { useMembers, useSpaces } from '../spaces/api';
import { useBacklog } from '../agile/api';
import { useCurrentUser } from '../../auth/AuthProvider';
import { uploadAttachment, useEpics, usePriorities, useIssueTypes, type IssueType } from './api';
import { RichTextEditor } from './RichTextEditor';

const allowedTypesOf = (types: IssueType[] | undefined, parent: unknown) =>
  (types ?? []).filter((type) => (parent ? type.hierarchy === 0 : type.hierarchy !== 0));

interface CreateIssueDialogProps {
  onClose: () => void;
  /** Set when adding a subtask from an issue page. */
  parent?: { id: string; projectKey: string };
  /** Opens as Create epic: the type is fixed and Assignee reads as Lead. */
  epic?: boolean;
}

export function CreateIssueDialog({ onClose, parent, epic }: CreateIssueDialogProps) {
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
  const me = useCurrentUser();
  const [reporter, setReporter] = useState<PickerUser | null>({ id: me.id, name: me.name });
  const [epicId, setEpicId] = useState('');
  const [points, setPoints] = useState('');
  const [labels, setLabels] = useState('');
  const [startDate, setStartDate] = useState('');
  const [dueDate, setDueDate] = useState('');
  const [another, setAnother] = useState(false);
  const [files, setFiles] = useState<File[]>([]);
  const fileInput = useRef<HTMLInputElement>(null);

  const addFiles = (event: ChangeEvent<HTMLInputElement>) => {
    setFiles((current) => [...current, ...Array.from(event.target.files ?? [])]);
    event.target.value = '';
  };

  const members = useMembers(projectId || undefined);
  const types = useIssueTypes(projectId || undefined);
  const priorities = usePriorities();
  const epics = useEpics(projectId || undefined);
  const backlog = useBacklog(projectId || undefined);
  const allowedTypes = allowedTypesOf(types.data, parent).filter((type) => !epic || type.hierarchy === 2);
  const [sprintId, setSprintId] = useState('');
  const canHaveEpic = !parent && allowedTypes.find((type) => type.id === typeId)?.hierarchy === 1;
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
            reporterId: reporter?.id ?? null,
            parentId: parent?.id ?? (canHaveEpic && epicId ? epicId : null),
            storyPoints: points ? Number(points) : null,
            labels: labels.split(',').map((item) => item.trim()).filter(Boolean),
            startDate: startDate || null,
            dueDate: dueDate || null,
          },
        }),
      ),
    onSuccess: async (issue) => {
      // The issue exists at this point, so a failed file must not lose it or block the rest.
      if (sprintId) {
        await unwrap(
          apiClient.POST('/api/v1/sprint/move-issue/{issueId}', { params: { path: { issueId: issue.id! } }, body: { sprintId } }),
        ).catch((failure: unknown) => toast('error', toMessage(failure)));
      }
      for (const file of files) {
        await uploadAttachment(issue.id!, file).catch((failure: unknown) => toast('error', `${file.name}: ${toMessage(failure)}`));
      }
      await queryClient.invalidateQueries({ queryKey: ['issues'] });
      toast('info', t('issues.created', { key: issue.key }));
      if (another) {
        setTitle('');
        setTitleTouched(false);
        setDescription('');
        setFiles([]);
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
      title={t(parent ? 'issues.addSubtask' : epic ? 'agile.createEpic' : 'issues.create')}
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
        <UserPicker label={t(epic ? 'agile.lead' : 'issues.assignee')} options={people} value={assignee} onChange={setAssignee} />
        <SelectField
          label={t('issues.priority')}
          value={priorityId || (priorities.data?.find((priority) => priority.name === 'Medium')?.id ?? '')}
          onChange={(event) => setPriorityId(event.target.value)}
          options={(priorities.data ?? []).map((priority) => ({ value: priority.id!, label: priority.name! }))}
        />
        <UserPicker label={t('issues.reporter')} options={people} value={reporter} onChange={setReporter} />
        <TextField label={t('issues.points')} type="number" value={points} onChange={(event) => setPoints(event.target.value)} />
        {canHaveEpic && (
          <SelectField
            label={t('issues.epic')}
            value={epicId}
            displayEmpty
            onChange={(event) => setEpicId(event.target.value)}
            options={[{ value: '', label: t('common.none') }, ...(epics.data?.items ?? []).map((epic) => ({ value: epic.id!, label: `${epic.key} ${epic.title}` }))]}
          />
        )}
        {canHaveEpic && backlog.data?.type === 0 && (
          <SelectField
            label={t('agile.sprint')}
            value={sprintId}
            displayEmpty
            onChange={(event) => setSprintId(event.target.value)}
            options={[{ value: '', label: t('common.none') }, ...(backlog.data.sprints ?? []).map((item) => ({ value: item.sprint!.id!, label: item.sprint!.name! }))]}
          />
        )}
        <TextField label={t('issues.labels')} value={labels} onChange={(event) => setLabels(event.target.value)} />
        <DatePicker label={t('issues.startDate')} value={startDate} onChange={setStartDate} />
        <DatePicker label={t('issues.dueDate')} value={dueDate} onChange={setDueDate} />
      </div>
      {dateError && <Typography role="alert" color="error" variant="body2">{dateError}</Typography>}
      <div className="flex flex-col items-start gap-1">
        <input ref={fileInput} type="file" multiple hidden aria-label={t('issues.attachments')} onChange={addFiles} />
        <Button kind="link" startIcon={<Paperclip size={16} strokeWidth={1.5} aria-hidden />} onClick={() => fileInput.current?.click()}>
          {t('issues.attachments')}
        </Button>
        {files.map((file, index) => (
          <div key={`${file.name}-${index}`} className="flex items-center gap-1">
            <Typography variant="body2">{file.name}</Typography>
            <IconButton
              size="small"
              aria-label={`${t('common.delete')} ${file.name}`}
              onClick={() => setFiles((current) => current.filter((_, position) => position !== index))}
            >
              <X size={16} strokeWidth={1.5} aria-hidden />
            </IconButton>
          </div>
        ))}
      </div>
      {create.isError && <Typography role="alert" color="error" variant="body2">{toMessage(create.error)}</Typography>}
    </Dialog>
  );
}
