import { notFound } from "next/navigation";
import { Button, ButtonLink, EmptyState, PageHeader, Panel, StatusBadge } from "../../src/ui";

export const metadata = { title: "UI kit" };

const statuses = [
  "Succeeded",
  "Healthy",
  "Ready",
  "Failed",
  "DeadLettered",
  "NeedsReview",
  "Stale",
  "Shadow",
  "Running",
  "Disabled",
  "Queued",
];

const colours = [
  ["--color-canvas", "Page background"],
  ["--color-surface", "Panels and tables"],
  ["--color-fill", "Quiet tiles"],
  ["--color-text-strong", "Body text"],
  ["--color-text-weak", "Secondary text"],
  ["--color-stroke-strong", "Field borders"],
  ["--color-stroke-weak", "Dividers"],
  ["--color-brand", "Interactive only"],
  ["--color-sidebar", "Sidebar"],
];

/**
 * The design system's reference page: every token and component in one place.
 * Development only. Rules: docs/design/backoffice-ui-redesign.md.
 */
export default function UiKitPage() {
  if (process.env.NODE_ENV !== "development") {
    notFound();
  }

  return (
    <main className="page-shell">
      <PageHeader
        eyebrow="Design system"
        title="UI kit"
        description="Every token and component, for building and reviewing new screens."
        actions={
          <>
            <Button variant="secondary">Export report</Button>
            <Button variant="primary">Import statement</Button>
          </>
        }
      />

      <div style={{ display: "grid", gap: "var(--space-l)" }}>
        <Panel title="Buttons" description="One primary per screen. Labels are a verb and a noun.">
          <div style={{ display: "flex", flexWrap: "wrap", gap: "var(--space-xs)" }}>
            <Button variant="primary">Run now</Button>
            <Button variant="secondary">View runs</Button>
            <Button variant="tertiary">Switch to shadow</Button>
            <ButtonLink href="/ui-kit" variant="secondary">Open automation</ButtonLink>
            <Button variant="primary" disabled>Submit invoice</Button>
          </div>
        </Panel>

        <Panel title="Status badges" description="Icon and word, so meaning never relies on colour.">
          <div style={{ display: "flex", flexWrap: "wrap", gap: "var(--space-xs)" }}>
            {statuses.map((status) => (
              <StatusBadge key={status} status={status} />
            ))}
          </div>
        </Panel>

        <Panel title="Colour tokens">
          <div
            style={{
              display: "grid",
              gap: "var(--space-s)",
              gridTemplateColumns: "repeat(auto-fill, minmax(200px, 1fr))",
            }}
          >
            {colours.map(([token, use]) => (
              <div key={token} style={{ display: "flex", gap: "var(--space-xs)", alignItems: "center" }}>
                <span
                  style={{
                    background: `var(${token})`,
                    border: "1px solid var(--color-stroke-weak)",
                    borderRadius: "var(--radius-s)",
                    height: 32,
                    width: 32,
                  }}
                />
                <span style={{ fontSize: "var(--text-small)" }}>
                  <code>{token}</code>
                  <br />
                  <span style={{ color: "var(--color-text-weak)" }}>{use}</span>
                </span>
              </div>
            ))}
          </div>
        </Panel>

        <Panel title="Empty state" flush>
          <EmptyState
            title="No statements imported yet"
            description="Download the OFX statement from Nedbank online banking, then import it here."
            action={<Button variant="primary">Import statement</Button>}
          />
        </Panel>
      </div>
    </main>
  );
}
