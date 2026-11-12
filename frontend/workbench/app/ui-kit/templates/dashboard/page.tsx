import { Upload } from "lucide-react";
import {
  ButtonLink,
  DashboardPage,
  DataTable,
  EmptyState,
  StatTile,
  StatusBadge,
  Tile,
  TileGrid,
  type Column,
} from "../../../../src/ui";
import { devOnly } from "../sample";

export const metadata = { title: "Dashboard template" };

type Run = { id: string; started: string; automation: string; status: string; result: string };

const runs: Run[] = [
  { id: "1", started: "11 Nov, 10:40", automation: "Invoice reconciliation", status: "Succeeded", result: "3 read · 1 new" },
  { id: "2", started: "11 Nov, 10:30", automation: "Stock sync", status: "Shadow", result: "54 checked · 6 changes · 0 written" },
  { id: "3", started: "11 Nov, 05:00", automation: "Investec feed", status: "Failed", result: "Acumatica sign-in timed out" },
];

const runColumns: Column<Run>[] = [
  { key: "started", header: "Started", render: (row) => row.started },
  { key: "automation", header: "Automation", render: (row) => row.automation },
  { key: "status", header: "Status", render: (row) => <StatusBadge status={row.status} /> },
  { key: "result", header: "Result", render: (row) => row.result, weak: true },
];

export default async function DashboardTemplateExample({
  searchParams,
}: {
  searchParams: Promise<{ clear?: string }>;
}) {
  devOnly();
  const { clear } = await searchParams;

  return (
    <DashboardPage
      header={{
        eyebrow: "Wednesday 11 November 2026",
        title: "Today",
        description: clear ? "Nothing needs you. Everything is running." : "3 items need you. Everything else is running.",
        actions: (
          <ButtonLink href="/ui-kit/templates/task" variant="primary">
            <Upload aria-hidden="true" />
            Import statement
          </ButtonLink>
        ),
      }}
      attention={{
        allClear: "Nothing needs you. Last checked 10:40.",
        link: { href: "/ui-kit/templates/list", label: "All exceptions" },
        items: clear
          ? []
          : [
              { id: "a", status: "Failed", title: "Investec feed failed at 05:00", detail: "Acumatica sign-in timed out. The next run is tomorrow at 05:00.", action: <ButtonLink href="/ui-kit/templates/record">Open automation</ButtonLink> },
              { id: "b", status: "NeedsReview", title: "INV190970 has no Shoprite PO number", detail: "Add the PO number in Acumatica. The invoice checks itself again on the next sync.", action: <ButtonLink href="/ui-kit/templates/record">Open invoice</ButtonLink> },
              { id: "c", status: "Overdue", tone: "warning", title: "Nedbank Western Cape: no statement since 7 Nov", detail: "Download the OFX from Nedbank and import it.", action: <ButtonLink href="/ui-kit/templates/task">Import statement</ButtonLink> },
            ],
      }}
      automations={{
        link: { href: "/ui-kit/templates/list", label: "All automations" },
        content: (
          <TileGrid min={190}>
            <Tile title="Investec feed" status={<StatusBadge status={clear ? "Healthy" : "Failing"} />} meta="Live · daily 05:00" href="/ui-kit/templates/record" />
            <Tile title="Invoice reconciliation" status={<StatusBadge status="Healthy" />} meta="Live · every 10 minutes" href="/ui-kit/templates/record" />
            <Tile title="Shoprite submission" status={<StatusBadge status="Shadow" />} meta="51 ready · 0 sent" href="/ui-kit/templates/record" />
            <Tile title="Stock sync" status={<StatusBadge status="Shadow" />} meta="Every 30 minutes · 6 changes" href="/ui-kit/templates/record" />
          </TileGrid>
        ),
      }}
      numbers={{
        content: (
          <TileGrid>
            <StatTile label="Shoprite invoices ready" value="51" detail="0 sent, submission in shadow" href="/ui-kit/templates/list" />
            <StatTile label="Needs review" value={clear ? "0" : "1"} detail="Missing PO number" tone={clear ? "default" : "warning"} href="/ui-kit/templates/list" />
            <StatTile label="Bank accounts current" value="3 of 4" detail="Nedbank Western Cape overdue" tone="warning" />
            <StatTile label="Last invoice sync" value="10:40" detail="Every 10 minutes" />
          </TileGrid>
        ),
      }}
      activity={{
        link: { href: "/ui-kit/templates/list", label: "All runs" },
        content: (
          <DataTable
            caption="Recent runs"
            columns={runColumns}
            rows={runs}
            rowKey={(row) => row.id}
            rowHref={() => "/ui-kit/templates/record"}
            empty={<EmptyState title="No runs yet" />}
          />
        ),
      }}
    />
  );
}
