import { CheckboxField, Notice, RadioGroup, SettingsPage, SettingsSection, TextField } from "../../../../src/ui";
import { demoAction, devOnly } from "../sample";

export const metadata = { title: "Settings template" };

export default function SettingsTemplateExample() {
  devOnly();

  return (
    <SettingsPage
      header={{
        eyebrow: "Banking",
        title: "Banking settings",
        description: "How bank statements arrive in Acumatica. Each section saves on its own.",
      }}
    >
      <SettingsSection
        title="Investec feed schedule"
        description="When the feed reads Investec and creates statements."
        action={demoAction}
        submitLabel="Save schedule"
        pendingLabel="Saving…"
        result={<Notice tone="success" title="Schedule saved at 10:52" />}
      >
        <RadioGroup
          name="schedule"
          label="When to run"
          required
          defaultValue="daily"
          options={[
            { value: "daily", label: "Daily at 05:00", hint: "Recommended. Investec posts the previous day's lines overnight" },
            { value: "twice", label: "Twice a day, 05:00 and 13:00" },
          ]}
        />
        <TextField name="days" label="Days to read again" hint="Each run reads this many days back, so a failed run loses nothing" type="number" width="s" required defaultValue="7" />
      </SettingsSection>

      <SettingsSection
        title="Overdue warnings"
        description="When an account shows as overdue on Today."
        action={demoAction}
        submitLabel="Save warnings"
        pendingLabel="Saving…"
      >
        <TextField name="overdue" label="Working days without a statement" type="number" width="s" required defaultValue="2" />
        <CheckboxField name="weekends" label="Count Saturdays as working days" />
      </SettingsSection>
    </SettingsPage>
  );
}
