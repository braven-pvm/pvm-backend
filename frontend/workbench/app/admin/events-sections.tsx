import { TabNav } from "../../src/ui";

/** The sections of Events: what arrived from Acumatica, and what moved through the queues. */
export function EventsSections({ current }: { current: "webhooks" | "messages" }) {
  return (
    <TabNav
      label="Event sources"
      tabs={[
        { label: "Acumatica webhooks", href: "/admin/acumatica-events", current: current === "webhooks" },
        { label: "Queue messages", href: "/admin/messages", current: current === "messages" },
      ]}
    />
  );
}
