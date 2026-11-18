"use client";

import { useActionState, useEffect, useRef, useState } from "react";
import { Plus } from "lucide-react";
import type { ScannerDevice } from "../../../src/api/scanner-devices";
import { formatDateTime } from "../../../src/formatters.mjs";
import { canIssueSetupCode, deviceStateLabel, maxScannerNameLength, notConfiguredMessage } from "../../../src/scanner-devices";
import { Button } from "../../../src/ui/button";
import { ConfirmDialog } from "../../../src/ui/confirm-dialog";
import { DataTable, type Column } from "../../../src/ui/data-table";
import { EmptyState } from "../../../src/ui/empty-state";
import { FormActions, FormLayout, TextField } from "../../../src/ui/field";
import { Notice } from "../../../src/ui/notice";
import { Panel } from "../../../src/ui/panel";
import { QrCode } from "../../../src/ui/qr-code";
import { Stack } from "../../../src/ui/stack";
import { StatusBadge } from "../../../src/ui/status-badge";
import { SubmitButton } from "../../../src/ui/submit-button";
import { issueSetupCodeAction, revokeDeviceAction, type SetupCodeState } from "./actions";

/**
 * The device list, Add device and the setup code panel. The setup code comes
 * back from the server action into this component's state and lives only in
 * memory: leaving or reloading the page drops it.
 */
export function DeviceEnrolment({ devices }: { devices: ScannerDevice[] }) {
  const [state, formAction] = useActionState(issueSetupCodeAction, { kind: "idle" } as SetupCodeState);
  const [hiddenKey, setHiddenKey] = useState<string | null>(null);
  const [expiredKey, setExpiredKey] = useState<string | null>(null);
  const resultRef = useRef<HTMLDivElement>(null);

  const codeKey = state.kind === "code" ? state.key : null;
  const codeExpiresAt = state.kind === "code" ? state.expiresAt : null;

  // Move to the result, so a person who pressed a row button sees the code.
  useEffect(() => {
    if (state.kind !== "idle") {
      resultRef.current?.focus();
    }
  }, [state]);

  // The code works for 15 minutes. After that the QR is taken off the screen.
  useEffect(() => {
    if (!codeKey || !codeExpiresAt) {
      return;
    }
    const left = Date.parse(codeExpiresAt) - Date.now();
    const timer = window.setTimeout(() => setExpiredKey(codeKey), Math.max(0, Number.isNaN(left) ? 15 * 60_000 : left));
    return () => window.clearTimeout(timer);
  }, [codeKey, codeExpiresAt]);

  // A device revoked since the code was made cannot use it.
  const codeDevice = state.kind === "code" ? devices.find((device) => device.id === state.deviceId) : undefined;
  const showCode = state.kind === "code" && hiddenKey !== state.key && codeDevice?.state !== "Revoked";

  const columns: Column<ScannerDevice>[] = [
    { key: "name", header: "Name", render: (device) => device.name },
    { key: "state", header: "State", render: (device) => <StatusBadge status={device.state} label={deviceStateLabel(device.state)} /> },
    { key: "created", header: "Created", render: (device) => formatDateTime(device.createdAt), weak: true },
    { key: "enrolled", header: "Set up", render: (device) => (device.enrolledAt ? formatDateTime(device.enrolledAt) : "Not yet"), weak: true },
    { key: "seen", header: "Last seen", render: (device) => (device.lastSeenAt ? formatDateTime(device.lastSeenAt) : "Never"), weak: true },
    {
      key: "actions",
      header: "Actions",
      render: (device) =>
        canIssueSetupCode(device.state) ? (
          <Stack direction="row" gap="xs" wrap>
            <form action={formAction}>
              <input type="hidden" name="intent" value="renew" />
              <input type="hidden" name="deviceId" value={device.id} />
              <SubmitButton variant="secondary" pendingLabel="Making code…">
                New setup code
              </SubmitButton>
            </form>
            <ConfirmDialog
              triggerLabel="Revoke"
              triggerVariant="tertiary"
              title={`Revoke ${device.name}?`}
              confirmLabel="Revoke device"
              pendingLabel="Revoking…"
              tone="danger"
              action={revokeDeviceAction}
              fields={{ deviceId: device.id, deviceName: device.name }}
            >
              <p>This phone stops working at once. To use it again, add it as a new device.</p>
            </ConfirmDialog>
          </Stack>
        ) : (
          <span>—</span>
        ),
    },
  ];

  return (
    <>
      <div ref={resultRef} tabIndex={-1} aria-live="polite">
        {showCode && state.kind === "code" ? (
          <Panel
            title={`Set up ${state.deviceName}`}
            description={expiredKey === state.key ? "This setup code has expired." : state.expiryText}
            actions={
              <Button variant="tertiary" onClick={() => setHiddenKey(state.key)}>
                Hide setup code
              </Button>
            }
          >
            {expiredKey === state.key ? (
              <Notice tone="warning" title="The setup code has expired">
                Make a new setup code for {state.deviceName} in the list below.
              </Notice>
            ) : (
              <Stack gap="s">
                <QrCode label={`Setup code for ${state.deviceName}`} svg={state.qrSvg} />
                <p>Open PVM Scanner on the phone and select Scan setup code.</p>
              </Stack>
            )}
          </Panel>
        ) : null}
        {state.kind === "notConfigured" ? (
          <Notice tone="error" title="No setup code was made" live>
            {notConfiguredMessage}
          </Notice>
        ) : null}
        {state.kind === "error" ? (
          <Notice tone="error" title="No setup code was made" live>
            {state.message}
          </Notice>
        ) : null}
      </div>
      <Panel flush title="Devices" description="Each phone that scans delivery notes. A revoked phone can no longer upload.">
        <DataTable
          caption="Scanner devices"
          columns={columns}
          rows={devices}
          rowKey={(device) => device.id}
          empty={<EmptyState title="No scanner devices yet" description="Add a device, then scan its setup code with the phone." />}
        />
      </Panel>
      <Panel title="Add device" description="Name the phone the way staff know it. You get a setup code to scan with it.">
        <form action={formAction}>
          <input type="hidden" name="intent" value="add" />
          <FormLayout>
            <TextField
              key={state.kind === "error" ? `error-${state.addName ?? ""}` : "add"}
              name="name"
              label="Device name"
              hint={`Such as “Dispatch phone 1”. At most ${maxScannerNameLength} characters.`}
              width="l"
              maxLength={maxScannerNameLength}
              defaultValue={state.kind === "error" ? state.addName : undefined}
              required
            />
            <FormActions>
              <SubmitButton variant="secondary" pendingLabel="Adding…">
                <Plus aria-hidden="true" />
                Add device
              </SubmitButton>
            </FormActions>
          </FormLayout>
        </form>
      </Panel>
    </>
  );
}
