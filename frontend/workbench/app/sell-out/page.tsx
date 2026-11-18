import { Upload } from "lucide-react";
import { redirect } from "next/navigation";
import {
  getSellOutImports,
  getUnmappedSellOutProducts,
  sellOutSources,
  type SellOutImportSummary,
  type UnmappedSellOutProduct,
} from "../../src/api/sell-out";
import { startEarly } from "../../src/api/early";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { formatStatementDate } from "../../src/formatters.mjs";
import { describeMonths, describeReceivedBy, describeSellOutRefusal } from "../../src/sell-out.mjs";
import { ButtonLink, DashboardPage, DataTable, EmptyState, StatTile, StatusBadge, TileGrid, type Column } from "../../src/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: "Sell-out" };

const importColumns: Column<SellOutImportSummary>[] = [
  { key: "when", header: "Received", render: (row) => formatStatementDate(row.createdAt.slice(0, 10)) },
  { key: "file", header: "File", render: (row) => sellOutSources[row.source] ?? row.fileName },
  { key: "months", header: "Months", render: (row) => (row.firstMonth ? describeMonths([row.firstMonth, row.lastMonth ?? row.firstMonth]) : "—") },
  { key: "rows", header: "Rows loaded", render: (row) => row.rowsLoaded, align: "end" },
  {
    key: "status",
    header: "Result",
    render: (row) =>
      row.status === "Loaded" ? (
        <StatusBadge status="Succeeded" label={row.rowsRefused > 0 ? `Loaded · ${row.rowsRefused} bad rows` : "Loaded"} />
      ) : (
        <StatusBadge status="Failed" label={row.status === "Refused" ? describeSellOutRefusal(row.statusReason).split(".")[0] : row.status} />
      ),
  },
  { key: "who", header: "By", render: (row) => describeReceivedBy(row.receivedBy) },
];

/** The sell-out overview: the latest imports, and the products that still need a map. */
export default async function SellOutPage() {
  const importsCall = startEarly(getSellOutImports());
  const unmappedCall = startEarly(getUnmappedSellOutProducts());
  const user = await requireWorkbenchUser("/sell-out");
  if (!hasAnyRole(user, ["Admin", "Operator"])) {
    redirect("/");
  }

  let imports: SellOutImportSummary[] = [];
  let unmapped: UnmappedSellOutProduct[] = [];
  let failed = false;
  try {
    [imports, unmapped] = await Promise.all([importsCall, unmappedCall]);
  } catch {
    failed = true;
  }

  const loaded = imports.filter((row) => row.status === "Loaded");
  const latestBySource = Object.keys(sellOutSources).map((source) => ({
    source,
    latest: loaded.find((row) => row.source === source),
  }));

  return (
    <DashboardPage
      header={{
        eyebrow: "Operations",
        title: "Sell-out",
        description: "What the stores of Makro and the Shoprite group, and the pharmacy wholesalers, sold of PVM products. Upload their reports here.",
        actions: (
          <ButtonLink href="/sell-out/import" variant="primary">
            <Upload aria-hidden="true" />
            Import a sell-out file
          </ButtonLink>
        ),
      }}
      attention={{
        allClear: "Every product in the loaded files maps to an Acumatica item.",
        link: { href: "/sell-out/mapping", label: "Map products" },
        items: [
          ...(failed
            ? [{ id: "api", status: "Failed", title: "The sell-out data could not be read", detail: "Try again in a few minutes." }]
            : []),
          ...(unmapped.length > 0
            ? [
                {
                  id: "unmapped",
                  status: "Pending",
                  title: `${unmapped.length} ${unmapped.length === 1 ? "product needs" : "products need"} a map`,
                  detail: "Their sales are loaded, but reports cannot show them by product until you choose the Acumatica item once.",
                  action: (
                    <ButtonLink href="/sell-out/mapping" variant="secondary">
                      Map products
                    </ButtonLink>
                  ),
                },
              ]
            : []),
        ],
      }}
      numbers={{
        title: "Latest file of each kind",
        content: (
          <TileGrid min={220}>
            {latestBySource.map(({ source, latest }) => (
              <StatTile
                key={source}
                label={sellOutSources[source]}
                value={latest?.lastMonth ? describeMonths([latest.lastMonth]) : "—"}
                detail={latest ? `Loaded ${formatStatementDate(latest.createdAt.slice(0, 10))}` : "Nothing loaded yet"}
              />
            ))}
          </TileGrid>
        ),
      }}
      activity={{
        title: "Latest imports",
        content: (
          <DataTable
            caption="Latest sell-out imports"
            columns={importColumns}
            rows={imports}
            rowKey={(row) => row.id}
            empty={<EmptyState title="No files imported yet" description="Imports show here after the first file." />}
          />
        ),
      }}
    />
  );
}
