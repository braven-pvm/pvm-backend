import Link from "next/link";
import { notFound } from "next/navigation";
import { Play } from "lucide-react";
import { enqueueIntegrationCommandAction } from "../../actions";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { findAutomation } from "../../../src/automations";
import { formatDateTime } from "../../../src/formatters.mjs";
import {
  ButtonLink,
  DataTable,
  EmptyState,
  KeyValueList,
  Notice,
  Panel,
  RecordPage,
  StatusBadge,
  SubmitButton,
  Timeline,
} from "../../../src/ui";
import { runColumns, runResult } from "../../run-columns";
import { loadSources, stateOf } from "../state";

export const dynamic = "force-dynamic";
export const metadata = { title: "Automation" };

type AutomationPageProps = {
  params: Promise<{ key: string }>;
  searchParams: Promise<{ queued?: string }>;
};

/** Run now is offered only where the backend has a command for it. */
const runNowCommands: Partial<Record<string, "acumatica-discovery">> = {
  "invoice-reading": "acumatica-discovery",
};

export default async function AutomationPage({ params, searchParams }: AutomationPageProps) {
  const { key } = await params;
  const { queued } = await searchParams;
  const user = await requireWorkbenchUser(`/automations/${key}`);
  const definition = findAutomation(key);
  if (!definition) {
    notFound();
  }

  const state = stateOf(definition, await loadSources());
  const command = runNowCommands[key];
  const canRun = command && hasAnyRole(user, ["Admin"]);
  const lastFailure = state.runs.find((run) => run.status === "Failed");

  return (
    <RecordPage
      back={{ href: "/automations", label: "Automations" }}
      header={{
        eyebrow: `${definition.module} · automation`,
        title: definition.name,
        description: definition.description,
        actions: (
          <>
            <StatusBadge status={state.health} />
            {canRun ? (
              <form action={enqueueIntegrationCommandAction}>
                <input type="hidden" name="command" value={command} />
                <input type="hidden" name="returnTo" value={`/automations/${key}`} />
                <SubmitButton pendingLabel="Queueing…">
                  <Play aria-hidden="true" />
                  Run now
                </SubmitButton>
              </form>
            ) : null}
          </>
        ),
      }}
      notice={
        <>
          {queued ? (
            <Notice tone="success" title="Run queued" live>
              The worker starts it within a minute. Reload this page to see the result.
            </Notice>
          ) : null}
          {state.health === "Failing" && lastFailure ? (
            <Notice tone="error" title={`Last run failed: ${lastFailure.errorSummary ?? lastFailure.errorCode ?? "no error recorded"}`}>
              The next scheduled run tries again. <Link href={`/runs/${lastFailure.id}`}>Open the run</Link> for the details.
            </Notice>
          ) : null}
          {state.health === "Late" ? (
            <Notice tone="warning" title="This automation is late">
              No successful run in two of its intervals. Check Runs for errors, and the worker logs if Runs is empty.
            </Notice>
          ) : null}
        </>
      }
      summary={
        <KeyValueList
          items={[
            { label: "Mode", value: state.currentMode },
            { label: "Runs", value: definition.schedule },
            { label: "Last result", value: state.lastRun },
            { label: "Module", value: definition.module },
            { label: "About the mode", value: definition.modeNote ?? null },
          ]}
        />
      }
      activity={
        state.runs.length > 0 ? (
          <Timeline
            label="Recent runs"
            entries={state.runs.slice(0, 6).map((run) => ({
              id: run.id,
              time: formatDateTime(run.startedAt ?? run.createdAt),
              dateTime: run.startedAt ?? run.createdAt,
              title: runResult(run),
              status: <StatusBadge status={run.status} />,
            }))}
          />
        ) : (
          <EmptyState
            title="No runs recorded here"
            description={definition.runFilter ? "Runs show here after the first scheduled run." : "This automation does not record its runs in the backoffice."}
          />
        )
      }
    >
      {state.runs.length > 0 ? (
        <Panel flush title="Runs" actions={<ButtonLink href="/runs" variant="tertiary">All runs</ButtonLink>}>
          <DataTable caption={`${definition.name} runs`} columns={runColumns} rows={state.runs} rowKey={(run) => run.id} rowHref={(run) => `/runs/${run.id}`} empty={null} />
        </Panel>
      ) : null}
    </RecordPage>
  );
}
