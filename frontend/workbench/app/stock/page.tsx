import { getLatestStockSyncRun, type StockSyncLine, type StockSyncRun } from "../../src/api/client";
import { startEarly } from "../../src/api/early";
import { requireWorkbenchUser } from "../../src/auth/session";
import { formatDateTime } from "../../src/formatters.mjs";
import {
  DashboardPage,
  DataTable,
  EmptyState,
  Notice,
  StatTile,
  StatusBadge,
  TileGrid,
  type AttentionItem,
  type Column,
  type StatusTone,
} from "../../src/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: "Stock" };

/** The job runs every 30 minutes. Past this, a person must know the levels are old. */
const STALE_AFTER_MINUTES = 90;

const actionOrder: Record<string, number> = { Update: 0, Skip: 1, Unchanged: 2 };

const quantity = (value: number | null) => (value === null ? "—" : String(value));
const level = (value: number | null) =>
  value === null ? "—" : Number.isInteger(value) ? String(value) : value.toFixed(1);

function decision(line: StockSyncLine, mode: string): { label: string; tone: StatusTone } {
  if (line.action === "Update") {
    return { label: mode === "Write" ? "Changed" : "Would change", tone: "info" };
  }
  if (line.action === "Skip") {
    return { label: "Skipped", tone: "warning" };
  }
  return { label: "Same", tone: "success" };
}

function columns(mode: string): Column<StockSyncLine>[] {
  return [
    { key: "sku", header: "SKU", render: (line) => line.sku, mono: true },
    {
      key: "decision",
      header: "Decision",
      render: (line) => {
        const { label, tone } = decision(line, mode);
        return <StatusBadge status={line.action} label={label} tone={tone} />;
      },
    },
    { key: "current", header: "Web store now", render: (line) => quantity(line.currentQuantity), align: "end" },
    { key: "level", header: "Acumatica level", render: (line) => level(line.acumaticaLevel), align: "end" },
    { key: "pending", header: "Open web orders", render: (line) => level(line.webPending), align: "end" },
    { key: "new", header: "Publishes", render: (line) => quantity(line.newQuantity), align: "end" },
    { key: "reason", header: "Why", render: (line) => line.reason ?? "", weak: true },
  ];
}

function attentionFor(run: StockSyncRun | null, readFailed: boolean, now: Date): AttentionItem[] {
  if (readFailed) {
    return [
      {
        id: "read",
        status: "Failed",
        title: "The stock sync history could not be read",
        detail: "Try again in a few minutes.",
      },
    ];
  }
  if (!run) {
    return [];
  }
  const items: AttentionItem[] = [];
  if (run.status === "Failed") {
    items.push({
      id: "failed",
      status: "Failed",
      title: `The last stock sync failed at ${formatDateTime(run.completedAt)}`,
      detail: run.detail ?? "The web store levels did not change in that run.",
    });
  }
  if (run.status === "Refused") {
    items.push({
      id: "refused",
      status: "Blocked",
      title: "The last stock sync stopped before it wrote anything",
      detail: run.detail ?? undefined,
    });
  }
  const minutesOld = (now.getTime() - new Date(run.completedAt).getTime()) / 60_000;
  if (minutesOld > STALE_AFTER_MINUTES) {
    items.push({
      id: "stale",
      status: "Late",
      title: `No stock sync since ${formatDateTime(run.completedAt)}`,
      detail: "The job runs every 30 minutes. The web store levels may be out of date.",
    });
  }
  return items;
}

function modeNotice(run: StockSyncRun | null) {
  if (!run || run.mode === "Write") {
    return undefined;
  }
  if (run.mode === "Disabled") {
    return (
      <Notice tone="info" title="The stock sync is off">
        It reads nothing and writes nothing to the web store.
      </Notice>
    );
  }
  return (
    <Notice tone="info" title="Shadow mode: the web store does not change">
      Each run calculates the levels and records what it would publish. Nothing is written until the mode is set to Write.
    </Notice>
  );
}

/** The Acumatica → WooCommerce stock sync: the latest run, and its decision for each product. */
export default async function StockPage() {
  const runCall = startEarly(getLatestStockSyncRun());
  await requireWorkbenchUser("/stock");

  // The page still renders when the API cannot be read, so the person sees why.
  let run: StockSyncRun | null = null;
  let readFailed = false;
  try {
    run = await runCall;
  } catch {
    readFailed = true;
  }

  const now = new Date();
  const lines = [...(run?.lines ?? [])].sort(
    (a, b) => (actionOrder[a.action] ?? 9) - (actionOrder[b.action] ?? 9) || a.sku.localeCompare(b.sku),
  );
  const shadow = run?.mode !== "Write";

  return (
    <DashboardPage
      header={{
        eyebrow: "Operations",
        title: "Stock",
        description: "Stock levels from Acumatica to the web store, every 30 minutes. A level under 5 shows as out of stock.",
      }}
      notice={modeNotice(run)}
      attention={{
        allClear: run ? `The last run finished at ${formatDateTime(run.completedAt)}.` : "No run is recorded yet.",
        items: attentionFor(run, readFailed, now),
      }}
      numbers={
        run
          ? {
              title: "Latest run",
              content: (
                <TileGrid min={200}>
                  <StatTile
                    label="Finished"
                    value={formatDateTime(run.completedAt)}
                    detail={`${run.mode} mode · ${run.itemCount} products`}
                    tone={run.status === "Succeeded" ? "default" : "error"}
                  />
                  <StatTile
                    label={shadow ? "Would change" : "Changed"}
                    value={String(shadow ? run.updateCount : run.writtenCount)}
                    detail={shadow ? "Not written: shadow mode" : `${run.updateCount} planned`}
                  />
                  <StatTile label="Same as the web store" value={String(run.unchangedCount)} />
                  <StatTile
                    label="Skipped"
                    value={String(run.skippedCount)}
                    detail="The web store does not take a level for these"
                    tone={run.skippedCount > 0 ? "warning" : "default"}
                  />
                </TileGrid>
              ),
            }
          : undefined
      }
      activity={{
        title: "Products in the latest run",
        content: (
          <DataTable
            caption="The stock sync decision for each product"
            columns={columns(run?.mode ?? "Shadow")}
            rows={lines}
            rowKey={(line) => `${line.productId}-${line.variationId ?? 0}`}
            empty={
              <EmptyState
                title={run ? "This run saw no products" : "No stock sync has run yet"}
                description={run ? run.detail ?? "The run read no products from the web store." : "Products show here after the first run."}
              />
            }
          />
        ),
      }}
    />
  );
}
