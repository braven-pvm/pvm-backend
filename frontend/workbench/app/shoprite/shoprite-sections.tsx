import { TabNav } from "../../src/ui";

type Section = "overview" | "invoices" | "purchase-orders" | "inventory-mappings";

/** The section tabs shared by the Shoprite list pages. Inventory mappings is Admin only. */
export function ShopriteSections({ current, isAdmin }: { current: Section; isAdmin: boolean }) {
  const tabs = [
    { key: "overview", label: "Overview", href: "/shoprite" },
    { key: "invoices", label: "Invoices", href: "/shoprite/invoices" },
    { key: "purchase-orders", label: "Purchase orders", href: "/shoprite/purchase-orders" },
    ...(isAdmin ? [{ key: "inventory-mappings", label: "Inventory mappings", href: "/shoprite/settings/inventory-mappings" }] : []),
  ];

  return (
    <TabNav
      label="Shoprite sections"
      tabs={tabs.map((tab) => ({ label: tab.label, href: tab.href, current: tab.key === current }))}
    />
  );
}
