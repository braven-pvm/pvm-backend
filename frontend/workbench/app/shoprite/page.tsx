import { getAutomationPolicy, getInvoiceCandidates, getShopriteCatalogItems, type InvoiceCandidateSummary } from "../../src/api/client";
import { startEarly } from "../../src/api/early";
import { hasAnyRole, requireWorkbenchUser } from "../../src/auth/session";
import { formatDateTime } from "../../src/formatters.mjs";
import {
  ButtonLink,
  DashboardPage,
  DataTable,
  EmptyState,
  StatTile,
  StatusBadge,
  Tile,
  TileGrid,
  type AttentionItem,
  type Column,
} from "../../src/ui";
import { ShopriteSections } from "./shoprite-sections";

export const dynamic = "force-dynamic";
export const metadata = { title: "Shoprite" };

const recentColumns: Column<InvoiceCandidateSummary>[] = [
  { key: "invoice", header: "Invoice", render: (row) => row.invoiceNumber, mono: true },
  { key: "location", header: "Store or DC", render: (row) => row.customerLocation ?? row.storeDcGln ?? "Not mapped" },
  { key: "updated", header: "Updated", render: (row) => formatDateTime(row.updatedAt), weak: true },
  { key: "status", header: "Status", render: (row) => <StatusBadge status={row.status} /> },
];

const count = (rows: InvoiceCandidateSummary[], status: string) => rows.filter((row) => row.status === status).length;

/** The Shoprite module overview: what needs a person, the automation, and the invoice numbers. */
export default async function ShopriteOverviewPage() {
  // The data calls start at once; the catalogue is an Admin endpoint, so it is read only for Admins.
  const candidatesCall = startEarly(getInvoiceCandidates());
  const policyCall = startEarly(getAutomationPolicy());
  const user = await requireWorkbenchUser("/shoprite");
  const isAdmin = hasAnyRole(user, ["Admin"]);
  const [candidates, policyView, catalog] = await Promise.all([
    candidatesCall,
    policyCall,
    isAdmin ? getShopriteCatalogItems() : Promise.resolve([]),
  ]);
  const policy = policyView.policy;
  const needsReview = count(candidates, "NeedsReview");
  const unmapped = catalog.filter((item) => !item.isMapped).length;

  const items: AttentionItem[] = [];
  if (policy.emergencyStop) {
    items.push({
      id: "stop",
      status: "Blocked",
      title: "Every Shoprite submission is blocked",
      detail: "Submissions by hand and by the automation are refused until an Admin clears the stop.",
      action: <ButtonLink href="/automations/shoprite-submission">Open automation</ButtonLink>,
    });
  }
  if (needsReview > 0) {
    items.push({
      id: "review",
      status: "NeedsReview",
      title: needsReview === 1 ? "1 invoice needs review" : `${needsReview} invoices need review`,
      detail: "Each one names the problem and where to fix it.",
      action: <ButtonLink href="/shoprite/invoices?status=NeedsReview">Open invoices</ButtonLink>,
    });
  }
  if (unmapped > 0) {
    items.push({
      id: "unmapped",
      status: "NeedsReview",
      title: unmapped === 1 ? "1 Shoprite item has no mapping" : `${unmapped} Shoprite items have no mapping`,
      detail: "Invoices with these items cannot be sent until they are mapped.",
      action: <ButtonLink href="/shoprite/settings/inventory-mappings">Open mappings</ButtonLink>,
    });
  }

  const recent = [...candidates].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)).slice(0, 5);

  return (
    <DashboardPage
      header={{
        eyebrow: "Operations",
        title: "Shoprite",
        description: "E-invoices built from finalised Acumatica invoices and sent to the Shoprite B2B portal.",
      }}
      sections={<ShopriteSections current="overview" isAdmin={isAdmin} />}
      attention={{ items, allClear: "Nothing in Shoprite needs you." }}
      automations={{
        content: (
          <TileGrid min={240}>
            <Tile
              title="Shoprite submission"
              status={<StatusBadge status={policy.emergencyStop ? "Blocked" : policy.mode} />}
              meta={`Policy v${policy.version} · ${policyView.decisionSummary.wouldSubmit} would send`}
              href="/automations/shoprite-submission"
            />
          </TileGrid>
        ),
      }}
      numbers={{
        title: "Invoices",
        content: (
          <TileGrid>
            <StatTile label="Ready" value={String(count(candidates, "Ready"))} detail="Pass every check" href="/shoprite/invoices?status=Ready" />
            <StatTile
              label="Need review"
              value={String(needsReview)}
              tone={needsReview > 0 ? "warning" : "default"}
              href="/shoprite/invoices?status=NeedsReview"
            />
            <StatTile label="Submitted" value={String(count(candidates, "Submitted"))} href="/shoprite/invoices?status=Submitted" />
            <StatTile label="All invoices" value={String(candidates.length)} href="/shoprite/invoices" />
          </TileGrid>
        ),
      }}
      activity={{
        title: "Recently changed invoices",
        link: { href: "/shoprite/invoices", label: "All invoices" },
        content: (
          <DataTable
            caption="Recently changed Shoprite invoices"
            columns={recentColumns}
            rows={recent}
            rowKey={(row) => row.id}
            rowHref={(row) => `/shoprite/invoices/${row.id}`}
            empty={<EmptyState title="No Shoprite invoices yet" />}
          />
        ),
      }}
    />
  );
}
