import { Plus } from "lucide-react";
import { listScannerDevices, listScannerStaff, type ScannerStaff } from "../../../src/api/scanner-devices";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { maxScannerNameLength } from "../../../src/scanner-devices";
import {
  ConfirmDialog,
  DataTable,
  EmptyState,
  FormActions,
  FormLayout,
  Notice,
  Panel,
  SettingsPage,
  Stack,
  StatusBadge,
  SubmitButton,
  TextField,
  type Column,
} from "../../../src/ui";
import { addStaffAction, updateStaffAction } from "./actions";
import { DeviceEnrolment } from "./device-enrolment";

export const dynamic = "force-dynamic";
export const metadata = { title: "Scanner devices" };

type DevicesPageProps = {
  searchParams: Promise<{ status?: string; error?: string }>;
};

const header = {
  eyebrow: "Documents",
  title: "Scanner devices",
  description: "The phones that scan delivery notes, and the staff names the phones offer.",
};

export default async function ScannerDevicesPage({ searchParams }: DevicesPageProps) {
  const user = await requireWorkbenchUser("/documents/devices");
  if (!hasAnyRole(user, ["Admin"])) {
    return (
      <SettingsPage header={header}>
        <Notice tone="info" title="Admin access needed">
          Only an Admin can add scanner devices and staff names.
        </Notice>
      </SettingsPage>
    );
  }

  const query = await searchParams;
  const [devices, staff] = await Promise.all([listScannerDevices(), listScannerStaff()]);

  return (
    <SettingsPage header={header}>
      {query.error ? (
        <Notice tone="error" title="Nothing changed" live>
          {query.error}
        </Notice>
      ) : null}
      {query.status ? <Notice tone="success" title={query.status} live /> : null}
      <DeviceEnrolment devices={devices} />
      <StaffNames staff={staff} />
    </SettingsPage>
  );
}

const staffColumns: Column<ScannerStaff>[] = [
  { key: "name", header: "Name", render: (row) => row.name },
  { key: "state", header: "State", render: (row) => <StatusBadge status={row.active ? "Active" : "Off"} /> },
  {
    key: "actions",
    header: "Actions",
    render: (row) => (
      <Stack direction="row" gap="xs" wrap>
        <ConfirmDialog
          triggerLabel="Rename"
          triggerVariant="tertiary"
          title={`Rename ${row.name}?`}
          confirmLabel="Rename"
          pendingLabel="Renaming…"
          action={updateStaffAction}
          fields={{ staffId: row.id, active: String(row.active), previousName: row.name, change: "name" }}
        >
          <p>Documents already uploaded keep the name they were scanned with.</p>
          <TextField
            name="name"
            id={`name-${row.id}`}
            label="New name"
            width="l"
            maxLength={maxScannerNameLength}
            defaultValue={row.name}
            required
          />
        </ConfirmDialog>
        <form action={updateStaffAction}>
          <input type="hidden" name="staffId" value={row.id} />
          <input type="hidden" name="name" value={row.name} />
          <input type="hidden" name="active" value={String(!row.active)} />
          <input type="hidden" name="change" value="state" />
          <SubmitButton variant="tertiary" pendingLabel={row.active ? "Turning off…" : "Turning on…"}>
            {row.active ? "Turn off" : "Turn on"}
          </SubmitButton>
        </form>
      </Stack>
    ),
  },
];

function StaffNames({ staff }: { staff: ScannerStaff[] }) {
  return (
    <>
      <Panel
        flush
        title="Staff names"
        description="Optional. Before a batch, the phone shows the names that are on, and the person taps theirs. A name has no password."
      >
        <DataTable
          caption="Staff names"
          columns={staffColumns}
          rows={staff}
          rowKey={(row) => row.id}
          empty={<EmptyState title="No staff names" description="The phones skip the name step until you add one." />}
        />
      </Panel>
      <Panel title="Add a staff name">
        <form action={addStaffAction}>
          <FormLayout>
            <TextField
              name="name"
              id="staff-name"
              label="Name"
              hint={`As the person is known at dispatch, such as “Thabo”. At most ${maxScannerNameLength} characters.`}
              width="l"
              maxLength={maxScannerNameLength}
              required
            />
            <FormActions>
              <SubmitButton variant="secondary" pendingLabel="Adding…">
                <Plus aria-hidden="true" />
                Add name
              </SubmitButton>
            </FormActions>
          </FormLayout>
        </form>
      </Panel>
    </>
  );
}
