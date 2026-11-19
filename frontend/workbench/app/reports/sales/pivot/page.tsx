import { getLabels, getPivot, listSavedFilters, type Pivot, type SavedFilter } from "../../../../src/api/sales-report";
import {
  apiQuery,
  dimensionLabel,
  dimensions,
  focusRow,
  formatChange,
  formatFigure,
  monthLabel,
  periodPresets,
  readState,
  shortFigure,
  stateQuery,
  toggleOpen,
  type Dimension,
  type ReportState,
} from "../../../../src/sales-report";
import {
  ButtonLink,
  ColumnChart,
  EmptyState,
  Notice,
  PageFrame,
  PageHeader,
  Panel,
  PivotGrid,
  Stack,
  StatTile,
  StatusBadge,
  TileGrid,
  type MultiSelectOption,
} from "../../../../src/ui";
import { basePath, freshness, isStale, reportHref, requireReportUser, single, type Search } from "../parts";
import { SalesTabs } from "../frame";

const pivotPath = `${basePath}/pivot`;
import { ReportControls } from "./report-controls";
import { SavedFilters } from "../saved-filters";

export const dynamic = "force-dynamic";
export const metadata = { title: "Sales report" };

const measureNames = { amount: "Sales (R)", quantity: "Quantity", customers: "Customers" } as const;

function changeBadge(current: number, previous: number) {
  const change = formatChange(current, previous);
  const tone = change.tone === "up" ? "success" : change.tone === "down" ? "error" : change.tone === "new" ? "info" : "neutral";
  return <StatusBadge status={change.tone} tone={tone} label={change.text} />;
}

/** The labels of the selected values, so that the filters show names. */
async function selectedLabels(state: ReportState) {
  const selected: Partial<Record<Dimension, MultiSelectOption[]>> = {};
  await Promise.all(
    dimensions
      .filter((dimension) => (state.include[dimension.id] ?? []).length > 0)
      .map(async (dimension) => {
        const keys = state.include[dimension.id] ?? [];
        const answer = await getLabels(dimension.id, keys).catch(() => ({ data: undefined }));
        const labels = new Map((answer.data ?? []).map((value) => [value.key, value.label]));
        selected[dimension.id] = keys.map((key) => ({ value: key, label: labels.get(key) ?? (key || "None") }));
      }),
  );
  return selected;
}

/**
 * The sales report: sell-in from Acumatica as a pivot. A person filters each
 * dimension by many values, sets the order of the drill-down, opens rows, and
 * saves the filter by name. Refer to docs/design/sales-report-pages.md.
 */
export default async function SalesReportPage({ searchParams }: { searchParams: Promise<Search> }) {
  await requireReportUser(pivotPath);
  const search = await searchParams;
  const state = readState(search);
  const savedId = single(search.saved);
  const savedError = single(search.savedError);

  let pivot: Pivot | undefined;
  let message: string | undefined;
  let saved: SavedFilter[] = [];
  let selected: Partial<Record<Dimension, MultiSelectOption[]>> = {};
  let failed = false;
  try {
    const [pivotAnswer, savedAnswer, labels] = await Promise.all([getPivot(apiQuery(state)), listSavedFilters(), selectedLabels(state)]);
    ({ data: pivot, message } = pivotAnswer);
    saved = savedAnswer.data ?? [];
    selected = labels;
  } catch {
    failed = true;
  }

  const current = saved.find((filter) => filter.id === savedId);
  const query = stateQuery(state, false).toString();
  const link = (next: ReportState) => reportHref(pivotPath, next, current?.id);
  const format = formatFigure;
  const period = state.from === state.to ? monthLabel(state.from) : `${monthLabel(state.from)} to ${monthLabel(state.to)}`;
  const levels = state.rows.map(dimensionLabel).join(", then ");
  const compare = state.compare && pivot?.totals.previousTotal !== undefined && pivot?.totals.previousTotal !== null;

  return (
    <PageFrame>
      <PageHeader
        eyebrow="Sales report"
        title={current ? `Pivot: ${current.name}` : "Pivot"}
        description={`Sell-in from Acumatica, with the rules of the sales workbook. ${pivot ? freshness(pivot.dataTo) : ""}`}
        actions={
          <Stack direction="row" wrap align="end" gap="xs">
            <SavedFilters path={pivotPath} filters={saved} current={current} query={query} />
            <ButtonLink href={`${basePath}/lines?${query}`}>Show the lines</ButtonLink>
            <ButtonLink href={`${basePath}/export?${query}`}>Export CSV</ButtonLink>
          </Stack>
        }
      />
      <Stack gap="l">
        <SalesTabs current="Pivot" query={query} />
        {failed ? (
          <Notice tone="error" title="The sales report could not be read">
            Try again in a few minutes.
          </Notice>
        ) : null}
        {message ? (
          <Notice tone="warning" title="The settings are not correct">
            {message}
          </Notice>
        ) : null}
        {savedError ? (
          <Notice tone="warning" title="The saved filter did not change" live>
            {savedError}
          </Notice>
        ) : null}
        {savedId && !current && !failed ? (
          <Notice tone="info" title="This saved filter is not available">
            Its owner deleted it or stopped sharing it. The page shows the filter of the link.
          </Notice>
        ) : null}
        {pivot && isStale(pivot.dataTo) ? (
          <Notice tone="warning" title="The sales data is more than a day old">
            {freshness(pivot.dataTo)} The nightly load runs at 04:00.
          </Notice>
        ) : null}

        <ReportControls path={pivotPath} state={state} selected={selected} presets={periodPresets()} saved={current?.id} />

        {pivot ? (
          <>
            <TileGrid min={220}>
              <StatTile label={`${measureNames[state.measure]}, ${period}`} value={state.measure === "amount" ? `R ${format(pivot.totals.total)}` : format(pivot.totals.total)} />
              {compare ? (
                <>
                  <StatTile
                    label="The same months a year before"
                    value={state.measure === "amount" ? `R ${format(pivot.totals.previousTotal ?? 0)}` : format(pivot.totals.previousTotal ?? 0)}
                  />
                  <StatTile label="Change" value={formatChange(pivot.totals.total, pivot.totals.previousTotal ?? 0).text} detail="Against the same months a year before" />
                </>
              ) : null}
              <StatTile
                label={dimensions.find((dimension) => dimension.id === state.rows[0])?.plural ?? "Rows"}
                value={String(pivot.rows.filter((row) => row.level === 0).length)}
                detail="With sales or credits in the period"
              />
            </TileGrid>

            {state.columns === "months" && pivot.columns.length > 1 && pivot.columns.length <= 12 ? (
              <Panel title={`${measureNames[state.measure]} by month`} description="Credit memos are subtracted. The grey columns are the same months a year before.">
                <ColumnChart
                  label={`${measureNames[state.measure]} by month`}
                  categories={pivot.columns.map((column) => column.label.slice(0, 3))}
                  series={[
                    { name: period, values: pivot.totals.values },
                    ...(compare && pivot.totals.previous ? [{ name: "A year before", values: pivot.totals.previous }] : []),
                  ]}
                  format={(value) => shortFigure(value, state.measure)}
                />
              </Panel>
            ) : null}

            <Panel flush title={`By ${levels.toLowerCase()}`} description="Open a row to see the next level. The filter icon on a row shows only that row.">
              <PivotGrid
                caption={`${measureNames[state.measure]} by ${levels.toLowerCase()}`}
                rowHeader={levels}
                headers={pivot.columns.map((column) => column.label)}
                trailingHeaders={compare ? ["A year before", "Change"] : []}
                rows={pivot.rows.map((row) => ({
                  key: row.key,
                  level: row.level,
                  label: row.label,
                  values: row.values.map(format),
                  total: format(row.total),
                  open: state.open.includes(row.key),
                  toggleHref: row.hasChildren ? link({ ...state, open: toggleOpen(state.open, row.key) }) : undefined,
                  action: { href: link(focusRow(state, row.key, row.level)), label: `Show only ${row.label}` },
                  trailing: compare ? [format(row.previousTotal ?? 0), changeBadge(row.total, row.previousTotal ?? 0)] : undefined,
                }))}
                totals={{
                  values: pivot.totals.values.map(format),
                  total: format(pivot.totals.total),
                  trailing: compare ? [format(pivot.totals.previousTotal ?? 0), changeBadge(pivot.totals.total, pivot.totals.previousTotal ?? 0)] : undefined,
                }}
                empty={
                  <EmptyState
                    title="No sales for these settings"
                    description="Choose another period, or clear some filters."
                    action={<ButtonLink href={pivotPath}>Clear all settings</ButtonLink>}
                  />
                }
              />
            </Panel>
          </>
        ) : null}
      </Stack>
    </PageFrame>
  );
}
