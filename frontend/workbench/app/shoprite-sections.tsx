import { TabNav } from "../src/ui";

type Section = "invoices" | "purchase-orders" | "inventory-mappings";

/** The section tabs shared by the Shoprite list pages. Inventory mappings is Admin only. */
export function ShopriteSections({ current, isAdmin }: { current: Section; isAdmin: boolean }) {
  const tabs = [
    { key: "invoices", label: "Invoices", href: "/invoices" },
    { key: "purchase-orders", label: "Purchase orders", href: "/purchase-orders" },
    ...(isAdmin ? [{ key: "inventory-mappings", label: "Inventory mappings", href: "/admin/inventory-mappings" }] : []),
  ];

  return (
    <TabNav
      label="Shoprite sections"
      tabs={tabs.map((tab) => ({ label: tab.label, href: tab.href, current: tab.key === current }))}
    />
  );
}
