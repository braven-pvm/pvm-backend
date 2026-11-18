import { getSalesReportOptions, getSalesReportOverview, type SalesReportOptions, type SalesReportOverview } from "../../../src/api/sales-report";
import { formatMoneyZar } from "../../../src/formatters.mjs";
import { financialYearOf, formatFigure, reportQuery } from "../../../src/sales-report";
import { ButtonLink, ColumnChart, DashboardPage, FilterChips, Notice, Panel, Stack, StatTile, TileGrid } from "../../../src/ui";
import { basePath, freshness, hrefWith, isStale, requireReportUser, SalesSections, type Search } from "./parts";

export const dynamic = "force-dynamic";
export const metadata = { title: "Sales report" };

/** "R 1.2 m", "R 850 k": short amounts for the chart axis. */
function shortRand(value: number) {
  if (Math.abs(value) >= 1_000_000) {
    return `R ${(value / 1_000_000).toFixed(1)} m`;
  }
  if (Math.abs(value) >= 1_000) {
    return `R ${Math.round(value / 1_000)} k`;
  }
  return `R ${formatFigure(value)}`;
}

function growth(thisYear: number, lastYear: number) {
  if (lastYear <= 0) {
    return "No sales in the same months last year";
  }
  const percent = ((thisYear - lastYear) / lastYear) * 100;
  return `${percent >= 0 ? "+" : ""}${percent.toFixed(1)}% against last year`;
}

/** The overview: this financial year to date against the same months last year. */
export default async function SalesOverviewPage({ searchParams }: { searchParams: Promise<Search> }) {
  await requireReportUser(basePath);
  const search = await searchParams;
  const year = financialYearOf(new Date());
  const query = reportQuery({ channel: search.channel });
  query.set("year", String(year));

  let overview: SalesReportOverview | undefined;
  let options: SalesReportOptions = { reps: [], provinces: [], itemGroups: [], channels: [] };
  let failed = false;
  try {
    [{ data: overview }, options] = await Promise.all([getSalesReportOverview(query), getSalesReportOptions()]);
  } catch {
    failed = true;
  }

  // Only the months that have sales this year count against last year, so that the comparison is fair.
  const months = overview?.months ?? [];
  const lastMonth = months.reduce((last, month, index) => (month.thisYear !== 0 ? index : last), -1);
  const sameMonthsLastYear = months.slice(0, lastMonth + 1).reduce((sum, month) => sum + month.lastYear, 0);

  return (
    <DashboardPage
      header={{
        eyebrow: "Reports",
        title: "Sales report",
        description: "Sell-in from Acumatica by financial year and month, with the rules of the sales workbook.",
      }}
      sections={<SalesSections current={basePath} search={search} />}
      notice={
        failed ? (
          <Notice tone="error" title="The sales report could not be read">
            Try again in a few minutes.
          </Notice>
        ) : undefined
      }
      attention={{
        items:
          overview && isStale(overview.dataTo)
            ? [
                {
                  id: "stale",
                  status: "Late",
                  tone: "warning",
                  title: "The sales data is more than a day old",
                  detail: `${freshness(overview.dataTo)} The nightly load runs at 04:00.`,
                  action: <ButtonLink href="/runs">Open the runs</ButtonLink>,
                },
              ]
            : [],
        allClear: overview ? freshness(overview.dataTo) : "The sales data is not available.",
      }}
      numbers={
        overview
          ? {
              title: `Financial year ${year}`,
              content: (
                <Stack gap="m">
                  {options.channels.length > 1 ? (
                    <FilterChips
                      label="Filter the sales by channel"
                      chips={[
                        { label: "All channels", href: basePath, current: !search.channel },
                        ...options.channels.map((channel) => ({ label: channel, href: hrefWith(basePath, {}, { channel }), current: search.channel === channel })),
                      ]}
                    />
                  ) : null}
                  <TileGrid min={220}>
                    <StatTile label="Sales this year to date" value={formatMoneyZar(overview.thisYearTotal)} detail={growth(overview.thisYearTotal, sameMonthsLastYear)} href={hrefWith(`${basePath}/by-rep`, {}, { years: String(year), channel: search.channel })} />
                    <StatTile label="Same months last year" value={formatMoneyZar(sameMonthsLastYear)} detail={`FY${year - 1} total ${formatMoneyZar(overview.lastYearTotal)}`} />
                    <StatTile label="Active customers" value={String(overview.activeCustomers)} detail="Customers with sales this year" href={hrefWith(`${basePath}/customers`, {}, { years: String(year), channel: search.channel })} />
                  </TileGrid>
                  <Panel title="Sales by financial month" description={`FY${year} against FY${year - 1}. Credit memos are subtracted.`}>
                    <ColumnChart
                      label="Sales by financial month"
                      categories={months.map((month) => month.label.slice(0, 3))}
                      series={[
                        { name: `FY${year}`, values: months.map((month) => month.thisYear) },
                        { name: `FY${year - 1}`, values: months.map((month) => month.lastYear) },
                      ]}
                      format={shortRand}
                    />
                  </Panel>
                </Stack>
              ),
            }
          : undefined
      }
    />
  );
}
