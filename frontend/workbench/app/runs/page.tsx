import { getIntegrationRuns } from "../../src/api/client";
import { requireWorkbenchUser } from "../../src/auth/session";
import { ButtonLink, DataTable, EmptyState, FilterChips, ListPage } from "../../src/ui";
import { runColumns } from "../run-columns";

export const dynamic = "force-dynamic";
export const metadata = { title: "Runs" };

type RunsPageProps = {
  searchParams: Promise<{ status?: string }>;
};

const filters = [
  { value: undefined, label: "All" },
  { value: "Failed", label: "Failed" },
  { value: "Running", label: "Running" },
  { value: "Succeeded", label: "Succeeded" },
] as const;

export default async function IntegrationRunsPage({ searchParams }: RunsPageProps) {
  await requireWorkbenchUser("/runs");
  const { status } = await searchParams;
  const runs = await getIntegrationRuns();
  const rows = status ? runs.filter((run) => run.status === status) : runs;

  return (
    <ListPage
      header={{
        eyebrow: "Automation",
        title: "Runs",
        description: "Every execution of every scheduled and manual job, newest first.",
      }}
      filters={
        <FilterChips
          label="Filter runs by status"
          chips={filters.map((filter) => ({
            label: filter.label,
            count: filter.value ? runs.filter((run) => run.status === filter.value).length : runs.length,
            href: filter.value ? `/runs?status=${filter.value}` : "/runs",
            current: status === filter.value,
          }))}
        />
      }
      table={
        <DataTable
          caption="Runs"
          columns={runColumns}
          rows={rows}
          rowKey={(run) => run.id}
          rowHref={(run) => `/runs/${run.id}`}
          empty={
            runs.length === 0 ? (
              <EmptyState title="No runs yet" description="Scheduled jobs record a run each time they start." />
            ) : (
              <EmptyState title="No runs match this filter" action={<ButtonLink href="/runs">Show all runs</ButtonLink>} />
            )
          }
        />
      }
    />
  );
}
