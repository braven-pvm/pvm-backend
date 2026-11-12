import type { NextConfig } from "next";

/**
 * Old URLs from before the Shoprite pages moved under /shoprite and the
 * automation policy moved under /automations. Bookmarks and
 * links in emails keep working. Remove these after a few months.
 */
const movedPaths = [
  { source: "/invoices", destination: "/shoprite/invoices" },
  { source: "/invoices/:id", destination: "/shoprite/invoices/:id" },
  { source: "/purchase-orders", destination: "/shoprite/purchase-orders" },
  { source: "/purchase-orders/:id", destination: "/shoprite/purchase-orders/:id" },
  { source: "/admin/inventory-mappings", destination: "/shoprite/settings/inventory-mappings" },
  { source: "/admin/inventory-mappings/new", destination: "/shoprite/settings/inventory-mappings/new" },
  { source: "/automation", destination: "/automations/shoprite-submission" },
];

const nextConfig: NextConfig = {
  devIndicators: false,
  async redirects() {
    return movedPaths.map((path) => ({ ...path, permanent: true }));
  },
};

export default nextConfig;
