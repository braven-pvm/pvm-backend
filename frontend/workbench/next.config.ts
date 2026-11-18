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
  { source: "/admin/acumatica-events", destination: "/events?source=webhook" },
  { source: "/admin/messages", destination: "/events?source=delivery" },
];

/**
 * Uploads go through server actions, which accept 1 MB by default. A Makro Dispo
 * report is about 1.6 MB, and the API accepts sell-out files up to 25 MB. The
 * limit adds room for the multipart encoding.
 */
export const serverActionBodySizeLimit = "26mb";

const nextConfig: NextConfig = {
  devIndicators: false,
  experimental: {
    serverActions: {
      bodySizeLimit: serverActionBodySizeLimit,
    },
  },
  async redirects() {
    return movedPaths.map((path) => ({ ...path, permanent: true }));
  },
};

export default nextConfig;
