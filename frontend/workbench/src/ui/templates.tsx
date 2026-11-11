import Link from "next/link";
import { CircleCheck } from "lucide-react";
import type { ReactNode } from "react";
import { AttentionList, type AttentionItem } from "./attention-list";
import { BackLink } from "./back-link";
import { FormActions, FormLayout } from "./field";
import { PageFrame } from "./page-frame";
import { PageHeader, type PageHeaderProps } from "./page-header";
import { Panel } from "./panel";
import { Stepper } from "./stepper";
import { SubmitButton } from "./submit-button";
import styles from "./templates.module.css";

/*
 * The five page templates. Every page is one of them. Each template fixes the
 * order of the parts and the space between them; the page supplies the
 * content. Spec: docs/design/design-system.md#page-templates
 */

type SectionLink = { href: string; label: string };

/** A titled block on a Dashboard page, with an optional "All …" link. */
export function DashboardSection({
  title,
  link,
  children,
}: {
  title: string;
  link?: SectionLink;
  children: ReactNode;
}) {
  const id = `section-${title.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`;
  return (
    <section className={styles.section} aria-labelledby={id}>
      <div className={styles.sectionHead}>
        <h2 className={styles.sectionTitle} id={id}>
          {title}
        </h2>
        {link ? (
          <Link className={styles.sectionLink} href={link.href}>
            {link.label}
          </Link>
        ) : null}
      </div>
      {children}
    </section>
  );
}

type DashboardPageProps = {
  header: PageHeaderProps;
  /** Shown above everything else, such as an emergency stop. */
  notice?: ReactNode;
  attention: {
    items: AttentionItem[];
    /** The one quiet line when nothing needs a person: "Nothing needs you. Last checked 10:40." */
    allClear: string;
    link?: SectionLink;
  };
  /** Tiles in a TileGrid, one per automation. */
  automations?: { content: ReactNode; link?: SectionLink };
  /** StatTiles in a TileGrid, or Panels side by side in a Columns. */
  numbers?: { title?: string; content: ReactNode };
  /** A Panel with a DataTable or a Timeline of recent runs. */
  activity?: { content: ReactNode; link?: SectionLink };
};

/** Today and each module overview. Order: attention, automations, numbers, activity. */
export function DashboardPage({ header, notice, attention, automations, numbers, activity }: DashboardPageProps) {
  return (
    <PageFrame>
      <PageHeader {...header} />
      <div className={styles.sections}>
        {notice}
        <DashboardSection title="Needs attention" link={attention.items.length > 0 ? attention.link : undefined}>
          {attention.items.length > 0 ? (
            <Panel flush>
              <AttentionList label="Needs attention" items={attention.items} />
            </Panel>
          ) : (
            <p className={styles.allClear}>
              <CircleCheck size={18} aria-hidden="true" />
              {attention.allClear}
            </p>
          )}
        </DashboardSection>
        {automations ? (
          <DashboardSection title="Automations" link={automations.link}>
            {automations.content}
          </DashboardSection>
        ) : null}
        {numbers ? <DashboardSection title={numbers.title ?? "Key numbers"}>{numbers.content}</DashboardSection> : null}
        {activity ? (
          <DashboardSection title="Recent activity" link={activity.link}>
            {activity.content}
          </DashboardSection>
        ) : null}
      </div>
    </PageFrame>
  );
}

type ListPageProps = {
  header: PageHeaderProps;
  /** A TabNav for the sections of the module, when the list is one of them. */
  sections?: ReactNode;
  /** A Notice about the whole list, such as "Order reading is switched off". */
  notice?: ReactNode;
  /**
   * A short second list of items that need a person before the main list is
   * useful, such as "Shoprite items without a mapping". A Panel, flush.
   */
  attention?: ReactNode;
  /** FilterChips or a FilterBar. */
  filters?: ReactNode;
  /** A DataTable. The template puts it in a flush Panel. */
  table: ReactNode;
  /** A Pagination. It goes in the same Panel, under the table. */
  pagination?: ReactNode;
};

/** A list of records with filters. Each row opens a Record page. */
export function ListPage({ header, sections, notice, attention, filters, table, pagination }: ListPageProps) {
  return (
    <PageFrame>
      <PageHeader {...header} />
      {sections}
      {notice}
      {attention}
      <div className={styles.list}>
        {filters}
        {table ? (
          <Panel flush>
            {table}
            {pagination}
          </Panel>
        ) : null}
      </div>
    </PageFrame>
  );
}

type RecordPageProps = {
  /**
   * The list this record belongs to: { href: "/shoprite/invoices", label: "Shoprite invoices" }.
   * Leave it out only for a record that has no list yet.
   */
  back?: SectionLink;
  /**
   * title is the identifier (INV190970). description is the one-line context.
   * actions: the StatusBadge first, then at most one primary button.
   */
  header: PageHeaderProps;
  /** Why the record needs a person, and what to do. */
  notice?: ReactNode;
  /** A KeyValueList of the key facts. */
  summary: ReactNode;
  /** A Timeline of what happened, newest first. */
  activity?: ReactNode;
  /** A TabNav for the record's parts (Details, Raw for Admin). */
  sections?: ReactNode;
  /** The detail panels: lines, payloads, related records. */
  children?: ReactNode;
};

/** One record: an invoice, a statement, a run, an automation. */
export function RecordPage({ back, header, notice, summary, activity, sections, children }: RecordPageProps) {
  return (
    <PageFrame>
      <div className={styles.recordHead}>
        {back ? <BackLink href={back.href} label={back.label} /> : null}
        <PageHeader {...header} />
      </div>
      {notice}
      <div className={styles.columns}>
        <Panel title="Summary">{summary}</Panel>
        {activity ? <Panel title="Activity">{activity}</Panel> : null}
      </div>
      {sections}
      {children}
    </PageFrame>
  );
}

type TaskPageProps = {
  back: SectionLink;
  header: PageHeaderProps;
  /** 3 to 5 short step names. The last one is the result: "Done". */
  steps: string[];
  /** The current step, counted from 0. */
  current: number;
  /** The content of the current step: one Panel, with its actions last. */
  children: ReactNode;
};

/**
 * A guided job: choose input, check what will change, confirm, see the result.
 * Anything that writes to Acumatica, Shoprite, a bank or the web store is a
 * Task page with a "check what will change" step.
 */
export function TaskPage({ back, header, steps, current, children }: TaskPageProps) {
  return (
    <PageFrame width="narrow">
      <div className={styles.recordHead}>
        <BackLink href={back.href} label={back.label} />
        <PageHeader {...header} />
      </div>
      <Stepper steps={steps} current={current} />
      {children}
    </PageFrame>
  );
}

/** Settings for one module or the whole backoffice: one SettingsSection per topic. */
export function SettingsPage({ header, children }: { header: PageHeaderProps; children: ReactNode }) {
  return (
    <PageFrame width="narrow">
      <PageHeader {...header} />
      {children}
    </PageFrame>
  );
}

type SettingsSectionProps = {
  title: string;
  description?: ReactNode;
  /** A server action that saves this section only. */
  action: (formData: FormData) => void | Promise<void>;
  /** "Save schedule". Never "Save" alone: there is one button per section. */
  submitLabel: string;
  pendingLabel: string;
  /** A Notice with the result of the last save, or the errors. */
  result?: ReactNode;
  /** Fields. */
  children: ReactNode;
};

/** One topic of settings with its own form and save button. */
export function SettingsSection({
  title,
  description,
  action,
  submitLabel,
  pendingLabel,
  result,
  children,
}: SettingsSectionProps) {
  return (
    <Panel title={title} description={description}>
      <form action={action}>
        <FormLayout>
          {result}
          {children}
          <FormActions>
            <SubmitButton variant="secondary" pendingLabel={pendingLabel}>
              {submitLabel}
            </SubmitButton>
          </FormActions>
        </FormLayout>
      </form>
    </Panel>
  );
}

/** Two or three Panels side by side; one column on a phone. */
export function Columns({ children }: { children: ReactNode }) {
  return <div className={styles.columns}>{children}</div>;
}
