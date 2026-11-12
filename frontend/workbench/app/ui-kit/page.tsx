import { notFound } from "next/navigation";
import { Play, Upload } from "lucide-react";
import {
  AttentionList,
  BackLink,
  BarList,
  Button,
  ButtonLink,
  CheckboxField,
  ConfirmDialog,
  DataTable,
  EmptyState,
  FileField,
  FilterChips,
  FormActions,
  FormLayout,
  KeyValueList,
  ModeSwitch,
  Notice,
  PageFrame,
  PageHeader,
  Pagination,
  Panel,
  RadioGroup,
  SelectField,
  Spinner,
  Stack,
  StatTile,
  StatusBadge,
  Stepper,
  SubmitButton,
  TabNav,
  Tabs,
  TextareaField,
  TextField,
  Tile,
  TileGrid,
  Timeline,
  type Column,
} from "../../src/ui";
import { ThemeSwitch } from "./theme-switch";
import styles from "./ui-kit.module.css";

export const metadata = { title: "UI kit" };

/*
 * The live reference for every token and component. Development only.
 * The written rules for each component: docs/design/design-system.md.
 * All data on this page is example data.
 */

async function demoAction() {
  "use server";
  await new Promise((resolve) => setTimeout(resolve, 800));
}

const sections = [
  ["foundations", "Foundations"],
  ["buttons", "Buttons"],
  ["status", "Status and notices"],
  ["fields", "Form fields"],
  ["tables", "Tables and filters"],
  ["navigation", "Tabs and navigation"],
  ["records", "Record parts"],
  ["dashboard", "Dashboard parts"],
  ["automation", "Automation controls"],
  ["feedback", "Loading and empty"],
] as const;

const colours = [
  ["--color-canvas", "Page background"],
  ["--color-surface", "Panels and tables"],
  ["--color-fill", "Table headers, quiet areas"],
  ["--color-text-strong", "Body text"],
  ["--color-text-weak", "Secondary text"],
  ["--color-stroke-strong", "Field borders"],
  ["--color-stroke-weak", "Dividers"],
  ["--color-brand", "Interactive only"],
  ["--color-brand-fill", "Hover and selected"],
  ["--color-sidebar", "Sidebar"],
  ["--color-error", "Error"],
  ["--color-warning", "Warning"],
  ["--color-success", "Success"],
  ["--color-info", "Info"],
] as const;

const spaces = ["2xs", "xs", "s", "m", "l", "xl", "xxl"] as const;
const spaceValues: Record<(typeof spaces)[number], number> = { "2xs": 4, xs: 8, s: 16, m: 24, l: 32, xl: 48, xxl: 80 };

const typeScale = [
  ["h1", "Page title", "28 / 36"],
  ["h2", "Dialog title, page section", "20 / 28"],
  ["h3", "Panel title", "16 / 24"],
  ["body", "Body text, field values", "16 / 24"],
  ["small", "Tables, hints, secondary text", "14 / 20"],
  ["caption", "Table headers, badges, eyebrows", "12 / 16"],
] as const;

type Invoice = { number: string; store: string; po: string | null; date: string; total: string; status: string };

const invoices: Invoice[] = [
  { number: "INV190970", store: "Shoprite Centurion DC", po: null, date: "10 Nov", total: "R 18 422.40", status: "NeedsReview" },
  { number: "INV190968", store: "Checkers Hyper Fourways", po: "1220104411", date: "10 Nov", total: "R 6 913.80", status: "Ready" },
  { number: "INV190961", store: "Shoprite Brackenfell DC", po: "1220102998", date: "09 Nov", total: "R 41 207.15", status: "Ready" },
  { number: "INV190955", store: "Checkers Durbanville", po: "1220102874", date: "09 Nov", total: "R 3 486.00", status: "Submitted" },
];

const invoiceColumns: Column<Invoice>[] = [
  { key: "number", header: "Invoice", render: (row) => row.number, mono: true, sortable: true },
  { key: "store", header: "Store or DC", render: (row) => row.store },
  { key: "po", header: "Shoprite PO", render: (row) => row.po ?? "Missing", mono: true },
  { key: "date", header: "Date", render: (row) => row.date, sortable: true },
  { key: "total", header: "Total incl. VAT", render: (row) => row.total, align: "end" },
  { key: "status", header: "Status", render: (row) => <StatusBadge status={row.status} /> },
];

const statuses = ["Succeeded", "Healthy", "Ready", "Failed", "DeadLettered", "NeedsReview", "Stale", "Shadow", "Running", "Disabled", "Queued"];

export default function UiKitPage() {
  if (process.env.NODE_ENV !== "development") {
    notFound();
  }

  return (
    <PageFrame>
      <Stack gap="xl">
        <PageHeader
          eyebrow="Design system"
          title="UI kit"
          description="Every token and component with its states. The rules are in docs/design/design-system.md. A screen uses only what is on this page."
          actions={
            <>
              <ButtonLink href="/ui-kit/templates">Page templates</ButtonLink>
              <ThemeSwitch />
            </>
          }
        />

        <nav aria-label="UI kit sections">
          <ul className={styles.index}>
            {sections.map(([id, label]) => (
              <li key={id}>
                <a href={`#${id}`}>{label}</a>
              </li>
            ))}
          </ul>
        </nav>

        <Section id="foundations" title="Foundations">
          <Panel title="Colour tokens" description="Brand colour on interactive elements only. Status colour always with an icon and a word.">
            <div className={styles.swatches}>
              {colours.map(([token, use]) => (
                <div className={styles.swatch} key={token}>
                  <span className={styles.chip} style={{ background: `var(${token})` }} />
                  <span>
                    <code>{token}</code>
                    <br />
                    <span className={styles.weak}>{use}</span>
                  </span>
                </div>
              ))}
            </div>
          </Panel>
          <Panel title="Spacing scale" description="8pt steps. Less space inside a group, more between groups.">
            <Stack gap="xs">
              {spaces.map((space) => (
                <div className={styles.spaceRow} key={space}>
                  <code>--space-{space}</code>
                  <span className={styles.spaceBar} style={{ width: `var(--space-${space})` }} />
                  <span className={styles.weak}>{spaceValues[space]}px</span>
                </div>
              ))}
            </Stack>
          </Panel>
          <Panel title="Type scale" description="Inter, ratio 1.2. Two weights: 400 and 600.">
            <Stack gap="s">
              {typeScale.map(([token, use, size]) => (
                <div className={styles.typeRow} key={token}>
                  <span style={{ fontSize: `var(--text-${token})`, lineHeight: `var(--leading-${token})`, fontWeight: token.startsWith("h") ? 600 : 400 }}>
                    Statement 001319 created
                  </span>
                  <span className={styles.weak}>
                    <code>--text-{token}</code> · {size} · {use}
                  </span>
                </div>
              ))}
            </Stack>
          </Panel>
        </Section>

        <Section id="buttons" title="Buttons">
          <Panel title="Button and ButtonLink" description="One primary per screen. Labels are a verb and a noun. ButtonLink when the action opens a page.">
            <Stack gap="m">
              <Stack direction="row" wrap gap="s">
                <Button variant="primary">
                  <Upload aria-hidden="true" />
                  Import statement
                </Button>
                <Button variant="secondary">Export CSV</Button>
                <Button variant="tertiary">Choose another file</Button>
                <ButtonLink href="/ui-kit#buttons">Open automation</ButtonLink>
              </Stack>
              <Stack direction="row" wrap gap="s">
                <Button variant="primary" disabled>
                  Disabled primary
                </Button>
                <Button variant="danger">Delete mapping (inside a dialog only)</Button>
              </Stack>
              <form action={demoAction}>
                <SubmitButton pendingLabel="Running…">
                  <Play aria-hidden="true" />
                  Run now (press to see the pending state)
                </SubmitButton>
              </form>
            </Stack>
          </Panel>
        </Section>

        <Section id="status" title="Status and notices">
          <Panel title="StatusBadge" description="Maps any status value to a tone. Icon and word, never colour alone.">
            <Stack direction="row" wrap gap="xs">
              {statuses.map((status) => (
                <StatusBadge key={status} status={status} />
              ))}
            </Stack>
          </Panel>
          <Panel title="Notice" description="A message about the page, or the result of an action (with live). There are no toasts.">
            <Stack gap="s">
              <Notice tone="info" title="Order reading is switched off">
                Reading orders through the API takes them off the Shoprite portal.
              </Notice>
              <Notice tone="success" title="Statement 001319 created" action={<ButtonLink href="/ui-kit#status">Open statement</ButtonLink>}>
                3 lines imported to PVMNEDWC. 1 line was already in Acumatica and was skipped.
              </Notice>
              <Notice tone="warning" title="Shoprite PO number is missing">
                Add the customer order number on the Acumatica sales order. The invoice checks itself again on the next sync.
              </Notice>
              <Notice tone="error" title="Last run failed: Acumatica sign-in timed out after 100 s">
                Nothing was written. The next run reads the same days again, so no transactions are lost.
              </Notice>
            </Stack>
          </Panel>
        </Section>

        <Section id="fields" title="Form fields">
          <Panel title="Fields" description="Label above, hint under the label, error under the hint. Fields without required show (optional).">
            <form action={demoAction}>
              <FormLayout>
                <TextField name="reference" label="Shoprite PO number" hint="10 digits, from the Shoprite portal" width="m" required inputMode="numeric" />
                <TextField name="from" label="From date" type="date" width="s" required error="Enter a date after 13 Sep 2026" />
                <SelectField
                  name="account"
                  label="Cash account"
                  width="m"
                  required
                  placeholder="Choose an account"
                  options={[
                    { value: "PVMINVEST", label: "PVMINVEST · Investec ZAR" },
                    { value: "PVMNEDBANK", label: "PVMNEDBANK · Nedbank Head Office" },
                  ]}
                />
                <RadioGroup
                  name="schedule"
                  label="When to run"
                  required
                  defaultValue="daily"
                  options={[
                    { value: "daily", label: "Daily at 05:00", hint: "Recommended for bank feeds" },
                    { value: "hourly", label: "Every hour" },
                    { value: "manual", label: "Only when started by hand" },
                  ]}
                />
                <CheckboxField name="zero" label="Include zero-value lines" hint="Acumatica ignores them, but they show in the preview" />
                <TextareaField name="note" label="Note for the audit log" hint="Why you made this change" />
                <FileField name="statement" label="Statement file" hint="Nedbank OFX, from online banking: Statements, Download, OFX" accept=".ofx" required />
                <FormActions>
                  <SubmitButton pendingLabel="Saving…">Save schedule</SubmitButton>
                  <ButtonLink href="/ui-kit#fields" variant="tertiary">
                    Cancel
                  </ButtonLink>
                </FormActions>
              </FormLayout>
            </form>
          </Panel>
        </Section>

        <Section id="tables" title="Tables and filters">
          <FilterChips
            label="Filter invoices by status"
            chips={[
              { label: "All", count: 52, href: "/ui-kit#tables", current: true },
              { label: "Ready", count: 51, href: "/ui-kit?status=ready#tables", current: false },
              { label: "Needs review", count: 1, href: "/ui-kit?status=review#tables", current: false },
              { label: "Sent", count: 0, href: "/ui-kit?status=sent#tables", current: false },
            ]}
          />
          <Panel flush>
            <DataTable
              caption="Shoprite invoices"
              columns={invoiceColumns}
              rows={invoices}
              rowKey={(row) => row.number}
              rowHref={() => "/ui-kit#records"}
              sort={{ key: "date", direction: "desc" }}
              sortHref={(key, direction) => `/ui-kit?sort=${key}&dir=${direction}#tables`}
              empty={<EmptyState title="No invoices yet" />}
            />
            <Pagination from={1} to={4} total={52} nextHref="/ui-kit?page=2#tables" />
          </Panel>
          <Panel flush title="DataTable with no rows">
            <DataTable
              caption="Empty example"
              columns={invoiceColumns}
              rows={[]}
              rowKey={(row) => row.number}
              empty={<EmptyState title="No invoices match this filter" description="Choose All to see every invoice." action={<ButtonLink href="/ui-kit#tables">Show all invoices</ButtonLink>} />}
            />
          </Panel>
        </Section>

        <Section id="navigation" title="Tabs and navigation">
          <Panel title="TabNav" description="Each tab is a URL. Use for module sections and record pages.">
            <TabNav
              label="Shoprite sections"
              tabs={[
                { label: "Invoices", href: "/ui-kit#navigation", current: true, count: 52 },
                { label: "Purchase orders", href: "/ui-kit?tab=po#navigation", current: false },
                { label: "Reference data", href: "/ui-kit?tab=ref#navigation", current: false },
              ]}
            />
          </Panel>
          <Panel title="Tabs" description="Panels inside one page, no URL. Arrow keys move between tabs.">
            <Tabs
              label="Invoice views"
              tabs={[
                { id: "lines", label: "Lines", content: <p className={styles.p}>The invoice lines show here.</p> },
                { id: "payload", label: "Submitted payload", content: <p className={styles.p}>A read-only summary of the payload shows here.</p> },
              ]}
            />
          </Panel>
          <Panel title="BackLink" description="The way up from a Record or Task page. The label names the list.">
            <BackLink href="/ui-kit#navigation" label="Shoprite invoices" />
          </Panel>
          <Panel title="Stepper" description="A Task page of 3 to 5 steps.">
            <Stack gap="m">
              <Stepper steps={["Choose file", "Check what will change", "Done"]} current={0} />
              <Stepper steps={["Choose file", "Check what will change", "Done"]} current={1} />
              <Stepper steps={["Choose file", "Check what will change", "Done"]} current={2} />
            </Stack>
          </Panel>
        </Section>

        <Section id="records" title="Record parts">
          <div className={styles.cols}>
            <Panel title="KeyValueList">
              <KeyValueList
                items={[
                  { label: "Customer", value: "DEB2062 · Shoprite Checkers" },
                  { label: "Deliver to", value: "Shoprite Centurion DC" },
                  { label: "GLN", value: "6001007000207", mono: true },
                  { label: "Shoprite PO", value: null },
                  { label: "VAT", value: "R 2 402.92" },
                ]}
              />
            </Panel>
            <Panel title="Timeline">
              <Timeline
                label="Invoice activity"
                entries={[
                  { id: "3", time: "10 Nov, 14:12", title: "Validation failed: no Shoprite PO number", status: <StatusBadge status="NeedsReview" /> },
                  { id: "2", time: "10 Nov, 14:12", title: "Read from Acumatica by invoice reconciliation" },
                  { id: "1", time: "10 Nov, 13:58", title: "Invoice finalised in Acumatica", detail: "By Izelle" },
                ]}
              />
            </Panel>
          </div>
        </Section>

        <Section id="dashboard" title="Dashboard parts">
          <Panel flush title="AttentionList" description="Sorted by severity automatically. One action each.">
            <AttentionList
              label="Needs attention"
              items={[
                { id: "a", status: "Shadow", title: "Stock sync found 6 changes", detail: "Review the differences before the sync writes to pvm.co.za.", action: <ButtonLink href="/ui-kit#dashboard">Review changes</ButtonLink> },
                { id: "b", status: "Failed", title: "Investec feed failed at 05:00", detail: "Acumatica sign-in timed out. The next run is tomorrow at 05:00.", action: <ButtonLink href="/ui-kit#dashboard">Open automation</ButtonLink> },
                { id: "c", status: "NeedsReview", title: "INV190970 has no Shoprite PO number", detail: "Add the PO number in Acumatica.", action: <ButtonLink href="/ui-kit#dashboard">Open invoice</ButtonLink> },
              ]}
            />
          </Panel>
          <TileGrid>
            <StatTile label="Ready to send" value="51" detail="0 sent, submission in shadow" href="/ui-kit#dashboard" />
            <StatTile label="Needs review" value="1" detail="Missing PO number" tone="warning" href="/ui-kit#dashboard" />
            <StatTile label="Failed runs today" value="1" detail="Investec feed" tone="error" />
            <StatTile label="Last invoice sync" value="10:40" detail="Every 10 minutes" />
          </TileGrid>
          <TileGrid min={190}>
            <Tile title="Investec feed" status={<StatusBadge status="Failing" />} meta="Live · daily 05:00" href="/ui-kit#dashboard" />
            <Tile title="Invoice reconciliation" status={<StatusBadge status="Healthy" />} meta="Live · every 10 minutes" href="/ui-kit#dashboard" />
            <Tile title="Shoprite submission" status={<StatusBadge status="Shadow" />} meta="51 ready · 0 sent" href="/ui-kit#dashboard" />
          </TileGrid>
          <Panel title="BarList" description="Compare one measure across up to 12 items. Numbers are always printed.">
            <BarList
              label="Days with statement lines in the last 30 days"
              max={30}
              axis={["0", "15", "30 days"]}
              bars={[
                { label: "Investec ZAR", value: 29, display: "29 / 30" },
                { label: "Nedbank Head Office", value: 28, display: "28 / 30" },
                { label: "Nedbank KZN", value: 26, display: "26 / 30" },
                { label: "Nedbank Western Cape", value: 21, display: "21 / 30", low: true },
              ]}
            />
          </Panel>
        </Section>

        <Section id="automation" title="Automation controls">
          <Panel title="ModeSwitch" description="Every change asks for an inline confirmation that says what the new mode does.">
            <ModeSwitch
              automation="Investec feed"
              current="Live"
              action={demoAction}
              consequences={{
                Off: "Off: no statements are created until the feed is switched on again.",
                Shadow: "Shadow: runs read Investec and log what they would import, but write nothing to Acumatica.",
                Live: "Live: each run creates statements on PVMINVEST in Acumatica.",
              }}
            />
          </Panel>
          <Panel title="ConfirmDialog" description="A modal for actions that are hard to undo. Destructive triggers are tertiary.">
            <Stack direction="row" wrap gap="s">
              <ConfirmDialog
                triggerLabel="Submit invoice"
                triggerVariant="primary"
                title="Submit INV190968 to Shoprite?"
                confirmLabel="Submit invoice"
                pendingLabel="Submitting…"
                action={demoAction}
                fields={{ invoice: "INV190968" }}
              >
                <p>Shoprite has no duplicate protection. A second submission creates a second invoice on their side.</p>
              </ConfirmDialog>
              <ConfirmDialog
                triggerLabel="Delete mapping"
                triggerVariant="tertiary"
                title="Delete the mapping for ENER12?"
                confirmLabel="Delete mapping"
                pendingLabel="Deleting…"
                tone="danger"
                action={demoAction}
              >
                <p>Invoices with ENER12 fail validation until you add the mapping again. This cannot be undone.</p>
              </ConfirmDialog>
            </Stack>
          </Panel>
        </Section>

        <Section id="feedback" title="Loading and empty">
          <Panel title="Spinner">
            <Stack direction="row" gap="l">
              <Spinner />
              <Spinner label="Reading statements from Investec…" />
            </Stack>
          </Panel>
          <Panel title="EmptyState" flush>
            <EmptyState
              title="No statements imported yet"
              description="Download the OFX statement from Nedbank online banking, then import it here."
              action={<Button variant="primary">Import statement</Button>}
            />
          </Panel>
        </Section>
      </Stack>
    </PageFrame>
  );
}

function Section({ id, title, children }: { id: string; title: string; children: React.ReactNode }) {
  return (
    <section id={id} className={styles.section} aria-labelledby={`${id}-title`}>
      <h2 className={styles.sectionTitle} id={`${id}-title`}>
        {title}
      </h2>
      <Stack gap="m">{children}</Stack>
    </section>
  );
}
