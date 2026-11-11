import { notFound } from "next/navigation";
import { StatusBadge, type Column } from "../../../src/ui";

/*
 * Example data for the template examples. Development only. None of it comes
 * from the API.
 */

export function devOnly() {
  if (process.env.NODE_ENV !== "development") {
    notFound();
  }
}

export async function demoAction() {
  "use server";
  await new Promise((resolve) => setTimeout(resolve, 800));
}

export type Invoice = {
  number: string;
  store: string;
  po: string | null;
  date: string;
  total: string;
  status: "NeedsReview" | "Ready" | "Submitted";
};

export const invoices: Invoice[] = [
  { number: "INV190970", store: "Shoprite Centurion DC", po: null, date: "10 Nov", total: "R 18 422.40", status: "NeedsReview" },
  { number: "INV190968", store: "Checkers Hyper Fourways", po: "1220104411", date: "10 Nov", total: "R 6 913.80", status: "Ready" },
  { number: "INV190961", store: "Shoprite Brackenfell DC", po: "1220102998", date: "09 Nov", total: "R 41 207.15", status: "Ready" },
  { number: "INV190955", store: "Checkers Durbanville", po: "1220102874", date: "09 Nov", total: "R 3 486.00", status: "Submitted" },
  { number: "INV190951", store: "Shoprite Midrand DC", po: "1220102761", date: "08 Nov", total: "R 27 905.60", status: "Ready" },
];

export const invoiceColumns: Column<Invoice>[] = [
  { key: "number", header: "Invoice", render: (row) => row.number, mono: true, sortable: true },
  { key: "store", header: "Store or DC", render: (row) => row.store },
  { key: "po", header: "Shoprite PO", render: (row) => row.po ?? "Missing", mono: true },
  { key: "date", header: "Date", render: (row) => row.date, sortable: true },
  { key: "total", header: "Total incl. VAT", render: (row) => row.total, align: "end" },
  { key: "status", header: "Status", render: (row) => <StatusBadge status={row.status} /> },
];

export const templateLinks = [
  { href: "/ui-kit/templates/dashboard", title: "Dashboard", use: "Today and each module overview" },
  { href: "/ui-kit/templates/list", title: "List", use: "Records with filters, sorting and pages" },
  { href: "/ui-kit/templates/record", title: "Record", use: "One invoice, statement, run or automation" },
  { href: "/ui-kit/templates/task", title: "Task", use: "A guided job that writes to another system" },
  { href: "/ui-kit/templates/settings", title: "Settings", use: "Forms, saved one section at a time" },
];
