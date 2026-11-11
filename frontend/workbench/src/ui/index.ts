// The PVM Backoffice component library. Reference: docs/design/design-system.md
// and /ui-kit in development. A screen uses only these components.
export { AppShell, type EnvironmentKind } from "./app-shell";
export { AttentionList, type AttentionItem } from "./attention-list";
export { BackLink } from "./back-link";
export { BarList, type Bar } from "./bar-list";
export { Button, ButtonLink, buttonClass, type ButtonVariant } from "./button";
export { CodeBlock } from "./code-block";
export { ConfirmDialog } from "./confirm-dialog";
export { DataTable, type Column, type SortDirection } from "./data-table";
export { EmptyState } from "./empty-state";
export {
  CheckboxField,
  FileField,
  FormActions,
  FormLayout,
  RadioGroup,
  SelectField,
  TextareaField,
  TextField,
  type FieldWidth,
  type Option,
} from "./field";
export { FilterBar } from "./filter-bar";
export { FilterChips, type FilterChip } from "./filter-chips";
export { KeyValueList, type KeyValue } from "./key-value-list";
export { ModeSwitch, type AutomationMode } from "./mode-switch";
export { Notice, type NoticeTone } from "./notice";
export { PageFrame } from "./page-frame";
export { PageHeader, type PageHeaderProps } from "./page-header";
export { PageError, PageLoading } from "./page-state";
export { Pagination } from "./pagination";
export { Panel } from "./panel";
export { Spinner } from "./spinner";
export { Stack, type Space } from "./stack";
export { StatTile, TileGrid } from "./stat-tile";
export { StatusBadge, statusLabel, toneFor, type StatusTone } from "./status-badge";
export { Stepper } from "./stepper";
export { SubmitButton } from "./submit-button";
export { TabNav, Tabs, type TabLink, type TabPanel } from "./tabs";
export {
  Columns,
  DashboardPage,
  DashboardSection,
  ListPage,
  RecordPage,
  SettingsPage,
  SettingsSection,
  TaskPage,
} from "./templates";
export { Tile } from "./tile";
export { Timeline, type TimelineEntry } from "./timeline";
