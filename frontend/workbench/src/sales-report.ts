/*
 * Helpers of the sales report pages. Refer to docs/design/sales-report-pages.md.
 * They have no React and no fetch, so the tests run in plain Node.
 */

export type SalesReportColumn = { financialYear: number; month: number; label: string };

/** The URL parameters of the report filters, in the order of the filter bar. */
export const filterNames = ["years", "channel", "rep", "province", "itemGroup", "product", "warehouse", "measure", "then"] as const;

/** The financial year of a date. The year runs from March to February: March 2026 is in 2027. */
export function financialYearOf(date: Date): number {
  return date.getUTCMonth() >= 2 ? date.getUTCFullYear() + 1 : date.getUTCFullYear();
}

export function readOpen(open: string | undefined): string[] {
  return (open ?? "")
    .split(",")
    .map((key) => key.trim())
    .filter((key) => key.length > 0);
}

/** Opens a row, or closes it and every row under it. */
export function toggleOpen(open: string[], key: string): string[] {
  if (!open.includes(key)) {
    return [...open, key];
  }
  return open.filter((item) => item !== key && !item.startsWith(`${key}|`));
}

/** The query string of the filters in use, for the API and for links. */
export function reportQuery(search: Record<string, string | undefined>): URLSearchParams {
  const query = new URLSearchParams();
  for (const name of filterNames) {
    const value = search[name]?.trim();
    if (value) {
      query.set(name, value);
    }
  }
  return query;
}

/** A figure in a report grid: whole rands or units, a space between thousands. */
export function formatFigure(value: number): string {
  const rounded = Math.sign(value) * Math.round(Math.abs(value));
  if (rounded === 0) {
    return "0";
  }
  const sign = rounded < 0 ? "-" : "";
  return sign + Math.abs(rounded).toString().replace(/\B(?=(\d{3})+(?!\d))/g, " ");
}

/**
 * Puts a total for each financial year after its 12 months, as the workbook pivots do.
 * Use it for amounts and quantities only: a count of distinct customers is not a sum.
 */
export function withYearTotals(columns: SalesReportColumn[], values: number[]) {
  const headers: string[] = [];
  const result: number[] = [];
  const yearHeaders: { label: string; span: number }[] = [];
  let index = 0;
  while (index < columns.length) {
    const year = columns[index].financialYear;
    let sum = 0;
    let span = 0;
    while (index < columns.length && columns[index].financialYear === year) {
      headers.push(columns[index].label);
      result.push(values[index] ?? 0);
      sum += values[index] ?? 0;
      index += 1;
      span += 1;
    }
    headers.push(`FY${year}`);
    result.push(sum);
    yearHeaders.push({ label: `FY${year}`, span: span + 1 });
  }
  return { headers, values: result, yearHeaders };
}
