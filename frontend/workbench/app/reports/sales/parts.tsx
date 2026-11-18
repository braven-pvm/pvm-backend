import { redirect } from "next/navigation";
import type { SalesReportOptions } from "../../../src/api/sales-report";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { formatStatementDate } from "../../../src/formatters.mjs";
import { financialYearOf, reportQuery } from "../../../src/sales-report";
import { FilterBar, FilterChips, SelectField, Stack, TabNav } from "../../../src/ui";

/*
 * The parts that the sales report pages share: the sections, the filters and
 * the freshness line. Refer to docs/design/sales-report-pages.md.
 */

export type Search = Record<string, string | undefined>;

export const basePath = "/reports/sales";

const sectionTabs = [
  { label: "Overview", href: basePath },
  { label: "By rep", href: `${basePath}/by-rep` },
  { label: "By product", href: `${basePath}/by-product` },
  { label: "Customers", href: `${basePath}/customers` },
  { label: "Data", href: `${basePath}/data` },
];

export async function requireReportUser(path: string) {
  const user = await requireWorkbenchUser(path);
  if (!hasAnyRole(user, ["Admin", "Operator"])) {
    redirect("/");
  }
  return user;
}

/** The section tabs. A tab keeps the filters, so that a person can compare the views. */
export function SalesSections({ current, search }: { current: string; search: Search }) {
  const query = reportQuery({ ...search, measure: undefined, then: undefined }).toString();
  return (
    <TabNav
      label="Sales report sections"
      tabs={sectionTabs.map((tab) => ({ ...tab, href: query ? `${tab.href}?${query}` : tab.href, current: tab.href === current }))}
    />
  );
}

/** A link to this page with some parameters changed. An empty value removes the parameter. */
export function hrefWith(path: string, search: Search, changes: Search) {
  const query = new URLSearchParams();
  for (const [name, value] of Object.entries({ ...search, ...changes })) {
    if (value) {
      query.set(name, value);
    }
  }
  const text = query.toString();
  return text ? `${path}?${text}` : path;
}

/** The choices of financial years: this year and the year before first, as the workbook shows. */
export function yearOptions(today = new Date()) {
  const current = financialYearOf(today);
  return [
    { value: `${current - 1},${current}`, label: `FY${current - 1} and FY${current}` },
    { value: `${current}`, label: `FY${current}` },
    { value: `${current - 1}`, label: `FY${current - 1}` },
    { value: `${current - 2},${current - 1}`, label: `FY${current - 2} and FY${current - 1}` },
    { value: `${current - 2},${current - 1},${current}`, label: `FY${current - 2} to FY${current}` },
  ];
}

/** The channel chips and the filter bar. */
export function SalesFilters({ path, search, options, keep = [] }: { path: string; search: Search; options: SalesReportOptions; keep?: string[] }) {
  const kept: Record<string, string> = {};
  for (const name of ["channel", ...keep]) {
    const value = search[name];
    if (value) {
      kept[name] = value;
    }
  }
  const active = ["rep", "province", "itemGroup"].filter((name) => search[name]).length + (search.years ? 1 : 0);
  const years = yearOptions();

  return (
    <Stack gap="s">
      {options.channels.length > 1 ? (
        <FilterChips
          label="Filter the sales by channel"
          chips={[
            { label: "All channels", href: hrefWith(path, search, { channel: undefined, open: undefined, page: undefined }), current: !search.channel },
            ...options.channels.map((channel) => ({
              label: channel,
              href: hrefWith(path, search, { channel, open: undefined, page: undefined }),
              current: search.channel === channel,
            })),
          ]}
        />
      ) : null}
      <FilterBar action={path} label="Filter the sales report" keep={kept} activeCount={active}>
        <SelectField name="years" label="Financial years" options={years} defaultValue={search.years ?? years[0].value} showOptional={false} />
        <SelectField name="rep" label="Rep" placeholder="All reps" options={options.reps} defaultValue={search.rep ?? ""} showOptional={false} />
        <SelectField
          name="province"
          label="Province"
          placeholder="All provinces"
          options={options.provinces.map((province) => ({ value: province, label: province }))}
          defaultValue={search.province ?? ""}
          showOptional={false}
        />
        <SelectField name="itemGroup" label="Item group" placeholder="All item groups" options={options.itemGroups} defaultValue={search.itemGroup ?? ""} showOptional={false} />
      </FilterBar>
    </Stack>
  );
}

/** "Sales to 18 November 2026, from the load at 02:05." */
export function freshness(dataTo: string | null | undefined) {
  if (!dataTo) {
    return "No sales load has finished yet.";
  }
  const time = new Date(dataTo);
  const clock = time.toLocaleTimeString("en-ZA", { hour: "2-digit", minute: "2-digit", hour12: false, timeZone: "Africa/Johannesburg" });
  return `Sales from the load of ${formatStatementDate(dataTo.slice(0, 10))} at ${clock}.`;
}

/** True when the last load is more than 36 hours old: the nightly load did not run. */
export function isStale(dataTo: string | null | undefined, now = new Date()) {
  return !dataTo || now.getTime() - new Date(dataTo).getTime() > 36 * 60 * 60 * 1000;
}
