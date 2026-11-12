import { redirect } from "next/navigation";
import {
  ButtonLink,
  DataTable,
  EmptyState,
  FileField,
  FormActions,
  FormLayout,
  KeyValueList,
  Notice,
  Panel,
  Stack,
  StatusBadge,
  SubmitButton,
  TaskPage,
  type Column,
} from "../../../../src/ui";
import { devOnly } from "../sample";

export const metadata = { title: "Task template" };

type PreviewLine = { id: string; date: string; description: string; amount: string; outcome: string };

const preview: PreviewLine[] = [
  { id: "1", date: "08 Nov", description: "ABSA BANK CAS1074 MidwaterSpar", amount: "R 3 919.43", outcome: "Import" },
  { id: "2", date: "08 Nov", description: "SAVEWAY SUPERSPAR", amount: "R 1 915.90", outcome: "Import" },
  { id: "3", date: "10 Nov", description: "SHELL MIDDELST", amount: "−R 1 597.95", outcome: "Import" },
  { id: "4", date: "10 Nov", description: "TRF NA CPG 10112026", amount: "R 2 971.07", outcome: "Skip" },
];

const previewColumns: Column<PreviewLine>[] = [
  { key: "date", header: "Date", render: (row) => row.date },
  { key: "description", header: "Description", render: (row) => row.description },
  { key: "amount", header: "Amount", render: (row) => row.amount, align: "end" },
  {
    key: "outcome",
    header: "Will",
    render: (row) =>
      row.outcome === "Import" ? (
        <StatusBadge status="Ready" label="Import" />
      ) : (
        <StatusBadge status="Skipped" tone="neutral" label="Skip · already imported" />
      ),
  },
];

const steps = ["Choose file", "Check what will change", "Done"];

async function goTo(formData: FormData) {
  "use server";
  await new Promise((resolve) => setTimeout(resolve, 600));
  redirect(`/ui-kit/templates/task?step=${formData.get("step")}`);
}

export default async function TaskTemplateExample({ searchParams }: { searchParams: Promise<{ step?: string }> }) {
  devOnly();
  const step = Math.min(2, Math.max(0, Number((await searchParams).step ?? 0) || 0));

  return (
    <TaskPage
      back={{ href: "/ui-kit/templates/dashboard", label: "Banking" }}
      header={{
        eyebrow: "Banking",
        title: "Import a bank statement",
        description: "Nedbank OFX statements. The import finds the account, renumbers the lines and skips lines already in Acumatica.",
      }}
      steps={steps}
      current={step}
    >
      {step === 0 ? (
        <Panel>
          <form action={goTo}>
            <input type="hidden" name="step" value="1" />
            <FormLayout>
              <FileField
                name="statement"
                label="Statement file"
                hint="Nedbank online banking: Statements, Download, OFX (Money 2003 or later)"
                accept=".ofx"
                required
              />
              <FormActions>
                <SubmitButton pendingLabel="Checking…">Check statement</SubmitButton>
              </FormActions>
            </FormLayout>
          </form>
        </Panel>
      ) : null}

      {step === 1 ? (
        <Stack gap="m">
          <Panel title="Statement">
            <KeyValueList
              items={[
                { label: "Account", value: "Nedbank Western Cape" },
                { label: "Cash account", value: "PVMNEDWC", mono: true },
                { label: "Period", value: "08–10 Nov 2026" },
                { label: "Balances", value: "R 31 004.10 to R 38 212.55 · the lines reconcile" },
              ]}
            />
          </Panel>
          <Panel flush title="What will change" description="3 lines are imported. 1 line is already in Acumatica and is skipped.">
            <DataTable
              caption="Statement lines to import"
              columns={previewColumns}
              rows={preview}
              rowKey={(row) => row.id}
              empty={<EmptyState title="The statement has no lines" />}
            />
          </Panel>
          <form action={goTo}>
            <input type="hidden" name="step" value="2" />
            <FormActions>
              <SubmitButton pendingLabel="Importing…">Import 3 lines</SubmitButton>
              <ButtonLink href="/ui-kit/templates/task" variant="tertiary">
                Choose another file
              </ButtonLink>
            </FormActions>
          </form>
        </Stack>
      ) : null}

      {step === 2 ? (
        <Stack gap="m">
          <Notice tone="success" title="Statement 001319 created" live>
            3 lines imported to PVMNEDWC. 1 line was already in Acumatica and was skipped.
          </Notice>
          <FormActions>
            <ButtonLink href="/ui-kit/templates/dashboard" variant="primary">
              Back to banking
            </ButtonLink>
            <ButtonLink href="/ui-kit/templates/task" variant="secondary">
              Import another statement
            </ButtonLink>
          </FormActions>
        </Stack>
      ) : null}
    </TaskPage>
  );
}
