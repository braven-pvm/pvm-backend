import { requireWorkbenchUser } from "../../src/auth/session";
import { DataTable, EmptyState, ListPage, StatusBadge, type Column } from "../../src/ui";
import { loadAutomations, type AutomationState } from "./state";

export const dynamic = "force-dynamic";
export const metadata = { title: "Automations" };

const modeTone = { Off: "neutral", Shadow: "info", Live: "success", Planned: "neutral" } as const;

const columns: Column<AutomationState>[] = [
  { key: "name", header: "Automation", render: (row) => row.name },
  { key: "module", header: "Module", render: (row) => row.module, weak: true },
  { key: "schedule", header: "Runs", render: (row) => row.schedule },
  { key: "mode", header: "Mode", render: (row) => <StatusBadge status={row.currentMode} tone={modeTone[row.currentMode]} /> },
  { key: "health", header: "Health", render: (row) => <StatusBadge status={row.health} /> },
  { key: "last", header: "Last result", render: (row) => row.lastRun, weak: true },
];

export default async function AutomationsPage() {
  await requireWorkbenchUser("/automations");
  const rows = await loadAutomations();

  return (
    <ListPage
      header={{
        eyebrow: "Automation",
        title: "Automations",
        description: "Every job that runs without a person. Off does nothing. Shadow works it out and writes nothing. Live writes.",
      }}
      table={
        <DataTable
          caption="Automations"
          columns={columns}
          rows={rows}
          rowKey={(row) => row.key}
          rowHref={(row) => `/automations/${row.key}`}
          empty={<EmptyState title="No automations" />}
        />
      }
    />
  );
}
