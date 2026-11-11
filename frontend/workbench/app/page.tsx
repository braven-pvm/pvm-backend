import { getOperationsSummary, type OperationsSummary } from "../src/api/client";
import { requireWorkbenchUser } from "../src/auth/session";
import { formatDateTime } from "../src/formatters.mjs";
import {
  ButtonLink,
  DashboardPage,
  DataTable,
  EmptyState,
  Notice,
  StatTile,
  StatusBadge,
  Tile,
  TileGrid,
  type AttentionItem,
} from "../src/ui";
import { runColumns } from "./run-columns";

export const dynamic = "force-dynamic";
export const metadata = { title: { absolute: "Today · PVM Backoffice" } };

/** What needs a person, from the operations summary. */
function attentionItems(view: OperationsSummary): AttentionItem[] {
  const items: AttentionItem[] = [];
  const { summary } = view;
  if (summary.failedRuns > 0) {
    items.push({
      id: "failed-runs",
      status: "Failed",
      title: summary.failedRuns === 1 ? "1 run failed in the last 24 hours" : `${summary.failedRuns} runs failed in the last 24 hours`,
      detail: "Open the run to see the error. Most runs try again on their next schedule.",
      action: <ButtonLink href="/runs">Open runs</ButtonLink>,
    });
  }
  if (summary.deadLetters > 0) {
    items.push({
      id: "dead-letters",
      status: "DeadLettered",
      title: summary.deadLetters === 1 ? "1 queue message stopped after repeated failures" : `${summary.deadLetters} queue messages stopped after repeated failures`,
      detail: "Replay each message when the cause is fixed, or close it with a reason.",
      action: <ButtonLink href="/exceptions?tab=dead-letters">Open dead letters</ButtonLink>,
    });
  }
  if (summary.needsReview > 0) {
    items.push({
      id: "needs-review",
      status: "NeedsReview",
      title: summary.needsReview === 1 ? "1 Shoprite invoice needs review" : `${summary.needsReview} Shoprite invoices need review`,
      detail: "Each one names the problem and where to fix it.",
      action: <ButtonLink href="/invoices?status=NeedsReview">Open invoices</ButtonLink>,
    });
  }
  if (view.acumaticaReconciliationFreshness.status === "Stale") {
    items.push({
      id: "reconciliation",
      status: "Late",
      title: "Invoice reading from Acumatica is late",
      detail: `The last successful read was ${formatDateTime(view.acumaticaReconciliationFreshness.lastSuccessfulReconciliationAt)}.`,
      action: <ButtonLink href="/runs">Open runs</ButtonLink>,
    });
  }
  return items;
}

export default async function HomePage() {
  await requireWorkbenchUser("/");
  const view = await getOperationsSummary();
  const items = attentionItems(view);
  const reconciliation = view.acumaticaReconciliationFreshness;
  const webhook = view.acumaticaPushNotificationHealth;

  return (
    <DashboardPage
      header={{
        eyebrow: formatDateTime(view.generatedAt),
        title: "Today",
        description:
          items.length === 0
            ? "Nothing needs you. Everything is running."
            : `${items.length === 1 ? "1 item needs" : `${items.length} items need`} you. Everything else is running.`,
      }}
      notice={
        view.automationEmergencyStop ? (
          <Notice tone="error" title="Emergency stop: every Shoprite submission is blocked" action={<ButtonLink href="/automation">Open automation</ButtonLink>}>
            Submissions by hand and by the automation are refused. Reading and checking invoices continues.
          </Notice>
        ) : null
      }
      attention={{
        items,
        allClear: `Nothing needs you. Last checked ${formatDateTime(view.generatedAt)}.`,
        link: { href: "/exceptions", label: "All exceptions" },
      }}
      automations={{
        link: { href: "/automation", label: "Shoprite automation" },
        content: (
          <TileGrid min={220}>
            <Tile
              title="Shoprite submission"
              status={<StatusBadge status={view.automationEmergencyStop ? "Blocked" : view.automationMode} />}
              meta={`Policy v${view.automationPolicyVersion}`}
              href="/automation"
            />
            <Tile
              title="Invoice reading"
              status={<StatusBadge status={reconciliation.status} />}
              meta={`Last read ${formatDateTime(reconciliation.lastSuccessfulReconciliationAt)}`}
              href="/runs"
            />
            <Tile
              title="Acumatica webhook"
              status={<StatusBadge status={webhook.status === "Healthy" ? "Healthy" : webhook.status === "Waiting" ? "Late" : "Off"} label={webhook.status === "NotConfigured" ? "Not configured" : undefined} />}
              meta={webhook.lastReceivedAt ? `Last event ${formatDateTime(webhook.lastReceivedAt)}` : "No events yet"}
              href="/admin/acumatica-events"
            />
          </TileGrid>
        ),
      }}
      numbers={{
        content: (
          <TileGrid>
            <StatTile label="Shoprite invoices" value={String(view.summary.candidateInvoices)} detail="Read from Acumatica" href="/invoices" />
            <StatTile
              label="Need review"
              value={String(view.summary.needsReview)}
              tone={view.summary.needsReview > 0 ? "warning" : "default"}
              href="/invoices?status=NeedsReview"
            />
            <StatTile label="Messages waiting" value={String(view.summary.pendingMessages)} detail={view.summary.activeRuns === 1 ? "1 run in progress" : `${view.summary.activeRuns} runs in progress`} href="/admin/messages" />
            <StatTile
              label="Dead letters"
              value={String(view.summary.deadLetters)}
              tone={view.summary.deadLetters > 0 ? "error" : "default"}
              href="/exceptions?tab=dead-letters"
            />
          </TileGrid>
        ),
      }}
      activity={{
        link: { href: "/runs", label: "All runs" },
        content: (
          <DataTable
            caption="Latest runs"
            columns={runColumns}
            rows={view.latestRuns.slice(0, 5)}
            rowKey={(run) => run.id}
            rowHref={(run) => `/runs/${run.id}`}
            empty={<EmptyState title="No runs yet" />}
          />
        ),
      }}
    />
  );
}
