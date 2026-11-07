// A namespace import, so Lucide names never clash with the themed exports; the bundler still keeps only what is used.
import * as Lucide from 'lucide-react';
import { useSyncExternalStore, type ComponentType } from 'react';

/**
 * Every icon the app draws, by what it means rather than how it looks. Components import the themed icons below, never
 * an icon library: a theme may bring its own set (see ThemeDefinition.icons), and Lucide, the Classic set, draws any
 * icon a theme's set leaves out. The ESLint rule no-restricted-imports keeps icon libraries out of the rest of the app.
 */
export const ICON_NAMES = [
  'close',
  'check',
  'delete',
  'add',
  'warning',
  'edit',
  'more',
  'chevronRight',
  'chevronDown',
  'chevronLeft',
  'chevronUp',
  'copy',
  'epic',
  'people',
  'user',
  'task',
  'loading',
  'filter',
  'link',
  'dragHandle',
  'pullRequest',
  'show',
  'hide',
  'back',
  'tool',
  'upload',
  'like',
  'board',
  'stop',
  'assistant',
  'reaction',
  'admin',
  'settings',
  'search',
  'reply',
  'attachment',
  'minus',
  'maximize',
  'signOut',
  'locked',
  'backlog',
  'list',
  'overview',
  'info',
  'inbox',
  'history',
  'commit',
  'branch',
  'document',
  'notFound',
  'priorityMedium',
  'subtask',
  'columns',
  'time',
  'help',
  'success',
  'error',
  'expandCollapse',
  'priorityHighest',
  'priorityLowest',
  'dateRange',
  'date',
  'bug',
  'bot',
  'story',
  'notifications',
  'arrowUp',
  'arrowDown',
  'mention',
  'inlineCode',
  'codeBlock',
  'heading1',
  'heading2',
  'heading3',
  'image',
  'callout',
  'taskList',
  'orderedList',
  'quote',
  'table',
  'text',
  'alignLeft',
  'alignCenter',
  'alignRight',
  'bold',
  'italic',
  'underline',
  'strikethrough',
  'highlight',
  'textColor',
  'clearFormatting',
  'splitColumns',
  'splitRows',
] as const;

export type IconName = (typeof ICON_NAMES)[number];

/** Lucide's props: size, strokeWidth and any SVG attribute. Sets from other libraries map them to their own. */
export type IconProps = Lucide.LucideProps;

export type IconComponent = ComponentType<IconProps>;

/** A theme's icons; names it leaves out fall back to Classic. */
export type IconSet = Partial<Record<IconName, IconComponent>>;

/** The Classic set, drawn with Lucide at a 1.5px stroke. */
export const classicIcons: Record<IconName, IconComponent> = {
  close: Lucide.X,
  check: Lucide.Check,
  delete: Lucide.Trash2,
  add: Lucide.Plus,
  warning: Lucide.TriangleAlert,
  edit: Lucide.Pencil,
  more: Lucide.Ellipsis,
  chevronRight: Lucide.ChevronRight,
  chevronDown: Lucide.ChevronDown,
  chevronLeft: Lucide.ChevronLeft,
  chevronUp: Lucide.ChevronUp,
  copy: Lucide.Copy,
  epic: Lucide.Zap,
  people: Lucide.Users,
  user: Lucide.User,
  task: Lucide.SquareCheck,
  loading: Lucide.LoaderCircle,
  filter: Lucide.ListFilter,
  link: Lucide.Link2,
  dragHandle: Lucide.GripVertical,
  pullRequest: Lucide.GitPullRequest,
  show: Lucide.Eye,
  hide: Lucide.EyeOff,
  back: Lucide.ArrowLeft,
  tool: Lucide.Wrench,
  upload: Lucide.Upload,
  like: Lucide.ThumbsUp,
  board: Lucide.SquareKanban,
  stop: Lucide.Square,
  assistant: Lucide.Sparkles,
  reaction: Lucide.SmilePlus,
  admin: Lucide.Shield,
  settings: Lucide.Settings,
  search: Lucide.Search,
  reply: Lucide.Reply,
  attachment: Lucide.Paperclip,
  minus: Lucide.Minus,
  maximize: Lucide.Maximize2,
  signOut: Lucide.LogOut,
  locked: Lucide.Lock,
  backlog: Lucide.ListTodo,
  list: Lucide.List,
  overview: Lucide.LayoutDashboard,
  info: Lucide.Info,
  inbox: Lucide.Inbox,
  history: Lucide.History,
  commit: Lucide.GitCommitHorizontal,
  branch: Lucide.GitBranch,
  document: Lucide.FileText,
  notFound: Lucide.FileQuestion,
  priorityMedium: Lucide.Equal,
  subtask: Lucide.CornerDownRight,
  columns: Lucide.Columns3,
  time: Lucide.Clock,
  help: Lucide.CircleHelp,
  success: Lucide.CircleCheck,
  error: Lucide.CircleAlert,
  expandCollapse: Lucide.ChevronsUpDown,
  priorityHighest: Lucide.ChevronsUp,
  priorityLowest: Lucide.ChevronsDown,
  dateRange: Lucide.CalendarDays,
  date: Lucide.Calendar,
  bug: Lucide.Bug,
  bot: Lucide.Bot,
  story: Lucide.Bookmark,
  notifications: Lucide.Bell,
  arrowUp: Lucide.ArrowUp,
  arrowDown: Lucide.ArrowDown,
  mention: Lucide.AtSign,
  inlineCode: Lucide.Code,
  codeBlock: Lucide.Code2,
  heading1: Lucide.Heading1,
  heading2: Lucide.Heading2,
  heading3: Lucide.Heading3,
  image: Lucide.ImageIcon,
  callout: Lucide.Lightbulb,
  taskList: Lucide.ListChecks,
  orderedList: Lucide.ListOrdered,
  quote: Lucide.Quote,
  table: Lucide.Table,
  text: Lucide.Type,
  alignLeft: Lucide.AlignLeft,
  alignCenter: Lucide.AlignCenter,
  alignRight: Lucide.AlignRight,
  bold: Lucide.Bold,
  italic: Lucide.Italic,
  underline: Lucide.Underline,
  strikethrough: Lucide.Strikethrough,
  highlight: Lucide.Highlighter,
  textColor: Lucide.Palette,
  clearFormatting: Lucide.RemoveFormatting,
  splitColumns: Lucide.Columns2,
  splitRows: Lucide.Rows2,
};

let active: IconSet = {};
const listeners = new Set<() => void>();

/** Puts a theme's set in use; an empty set means Classic. */
export function setIconSet(set: IconSet) {
  active = set;
  listeners.forEach((listener) => listener());
}

const subscribe = (listener: () => void) => {
  listeners.add(listener);
  return () => listeners.delete(listener);
};

/** The icon drawn for a name in the theme in use. */
export function useIcon(name: IconName): IconComponent {
  const set = useSyncExternalStore(subscribe, () => active);
  return set[name] ?? classicIcons[name];
}

const themed = (name: IconName): IconComponent => {
  function ThemedIcon(props: IconProps) {
    const Drawn = useIcon(name);
    return <Drawn strokeWidth={1.5} {...props} />;
  }
  ThemedIcon.displayName = `${name}Icon`;
  return ThemedIcon;
};

/** The SVG attributes of icon props, without Lucide's own stroke settings, for sets from other libraries. */
export function svgAttributes(props: Omit<IconProps, 'size'>): Record<string, unknown> {
  const attributes: Record<string, unknown> = { ...props };
  delete attributes.strokeWidth;
  delete attributes.absoluteStrokeWidth;
  return attributes;
}

/** One icon by name, for places that pick it at run time. */
export function Icon({ name, ...props }: IconProps & { name: IconName }) {
  const Drawn = useIcon(name);
  return <Drawn strokeWidth={1.5} {...props} />;
}

export const CloseIcon = themed('close');
export const CheckIcon = themed('check');
export const DeleteIcon = themed('delete');
export const AddIcon = themed('add');
export const WarningIcon = themed('warning');
export const EditIcon = themed('edit');
export const MoreIcon = themed('more');
export const ChevronRightIcon = themed('chevronRight');
export const ChevronDownIcon = themed('chevronDown');
export const ChevronLeftIcon = themed('chevronLeft');
export const ChevronUpIcon = themed('chevronUp');
export const CopyIcon = themed('copy');
export const EpicIcon = themed('epic');
export const PeopleIcon = themed('people');
export const UserIcon = themed('user');
export const TaskIcon = themed('task');
export const LoadingIcon = themed('loading');
export const FilterIcon = themed('filter');
export const LinkIcon = themed('link');
export const DragHandleIcon = themed('dragHandle');
export const PullRequestIcon = themed('pullRequest');
export const ShowIcon = themed('show');
export const HideIcon = themed('hide');
export const BackIcon = themed('back');
export const ToolIcon = themed('tool');
export const UploadIcon = themed('upload');
export const LikeIcon = themed('like');
export const BoardIcon = themed('board');
export const StopIcon = themed('stop');
export const AssistantIcon = themed('assistant');
export const ReactionIcon = themed('reaction');
export const AdminIcon = themed('admin');
export const SettingsIcon = themed('settings');
export const SearchIcon = themed('search');
export const ReplyIcon = themed('reply');
export const AttachmentIcon = themed('attachment');
export const MinusIcon = themed('minus');
export const MaximizeIcon = themed('maximize');
export const SignOutIcon = themed('signOut');
export const LockedIcon = themed('locked');
export const BacklogIcon = themed('backlog');
export const ListIcon = themed('list');
export const OverviewIcon = themed('overview');
export const InfoIcon = themed('info');
export const InboxIcon = themed('inbox');
export const HistoryIcon = themed('history');
export const CommitIcon = themed('commit');
export const BranchIcon = themed('branch');
export const DocumentIcon = themed('document');
export const NotFoundIcon = themed('notFound');
export const PriorityMediumIcon = themed('priorityMedium');
export const SubtaskIcon = themed('subtask');
export const ColumnsIcon = themed('columns');
export const TimeIcon = themed('time');
export const HelpIcon = themed('help');
export const SuccessIcon = themed('success');
export const ErrorIcon = themed('error');
export const ExpandCollapseIcon = themed('expandCollapse');
export const PriorityHighestIcon = themed('priorityHighest');
export const PriorityLowestIcon = themed('priorityLowest');
export const DateRangeIcon = themed('dateRange');
export const DateIcon = themed('date');
export const BugIcon = themed('bug');
export const BotIcon = themed('bot');
export const StoryIcon = themed('story');
export const NotificationsIcon = themed('notifications');
export const ArrowUpIcon = themed('arrowUp');
export const ArrowDownIcon = themed('arrowDown');
export const MentionIcon = themed('mention');
export const InlineCodeIcon = themed('inlineCode');
export const CodeBlockIcon = themed('codeBlock');
export const Heading1Icon = themed('heading1');
export const Heading2Icon = themed('heading2');
export const Heading3Icon = themed('heading3');
export const ImageIcon = themed('image');
export const CalloutIcon = themed('callout');
export const TaskListIcon = themed('taskList');
export const OrderedListIcon = themed('orderedList');
export const QuoteIcon = themed('quote');
export const TableIcon = themed('table');
export const TextIcon = themed('text');
export const AlignLeftIcon = themed('alignLeft');
export const AlignCenterIcon = themed('alignCenter');
export const AlignRightIcon = themed('alignRight');
export const BoldIcon = themed('bold');
export const ItalicIcon = themed('italic');
export const UnderlineIcon = themed('underline');
export const StrikethroughIcon = themed('strikethrough');
export const HighlightIcon = themed('highlight');
export const TextColorIcon = themed('textColor');
export const ClearFormattingIcon = themed('clearFormatting');
export const SplitColumnsIcon = themed('splitColumns');
export const SplitRowsIcon = themed('splitRows');
