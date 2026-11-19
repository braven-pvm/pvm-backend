import type { ReactNode } from "react";
import { getLabels, getMeta, listSavedFilters, type SavedFilter } from "../../../src/api/sales-report";
import {
  dimensionLabel,
  dimensions,
  periodNote,
  periodPresets,
  prototypePeriods,
  readState,
  readTrail,
  stateQuery,
  withTrail,
  type Dimension,
  type ReportState,
  type TrailStep,
} from "../../../src/sales-report";
import { BackLink, ButtonLink, FilterChips, Notice, PageFrame, PageHeader, Stack, TabNav, type MultiSelectOption } from "../../../src/ui";
import { byDimension } from "./data";
import { FilterBar } from "./filter-bar";
import { pageHref } from "./links";
import { basePath, freshness, isStale, single, type Search } from "./parts";
import { SavedFilters } from "./saved-filters";

/*
 * The frame of the prototype pages: the header with the saved filters and the download, the
 * section tabs, and the filter bar. Refer to docs/design/sales-report-pages.md.
 */

export type Context = {
  /** The filters of the person, without the trail and the page. Saved filters keep this. */
  base: ReportState;
  /** The filters with the trail and the page: what the page shows. */
  state: ReportState;
  trail: TrailStep[];
  saved: SavedFilter[];
  current?: SavedFilter;
  savedError?: string;
};

/** Reads the URL. pageStep is the dimension and key of a detail page, such as the rep of a rep page. */
export async function readContext(search: Search, pageStep?: TrailStep): Promise<Context> {
  const base = readState(search);
  const trail = readTrail(search.trail).filter((step) => step.dimension !== pageStep?.dimension);
  const state = withTrail(base, pageStep ? [...trail, pageStep] : trail);
  const savedId = single(search.saved);
  const saved = (await listSavedFilters().catch(() => ({ data: undefined }))).data ?? [];
  return { base, state, trail, saved, current: saved.find((filter) => filter.id === savedId), savedError: single(search.savedError) };
}

/** A link to a detail page one step further down: the trail gets the step of this page. */
export function drillHref(context: Context, path: string, here?: TrailStep, extra?: Record<string, string>) {
  return pageHref(path, context.base, { trail: here ? [...context.trail, here] : context.trail, saved: context.current?.id, extra });
}

/** A link to a section page with the same filters. The trail stays out. */
export function sectionHref(context: Context, path: string, extra?: Record<string, string>) {
  return pageHref(path, context.base, { saved: context.current?.id, extra });
}

const tabs = [
  { label: "Overview", path: basePath },
  { label: "Reps", path: `${basePath}/reps` },
  { label: "Customers", path: `${basePath}/customers` },
  { label: "Products", path: `${basePath}/products` },
  { label: "Pivot", path: `${basePath}/pivot` },
];

/** The section tabs for a page that is not in a SalesFrame, such as the pivot. */
export function SalesTabs({ current, query }: { current: string; query: string }) {
  return <TabNav label="Sales report" tabs={tabs.map((item) => ({ label: item.label, href: query ? `${item.path}?${query}` : item.path, current: item.label === current }))} />;
}

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

type SalesFrameProps = {
  context: Context;
  /** The path of this page, for the filter bar and the saved filters. */
  path: string;
  /** The section tab, on a section page. A detail page has a back link. */
  tab?: string;
  back?: { href: string; label: string };
  eyebrow: string;
  title: string;
  description: ReactNode;
  /** The dimensions that the page fixes, such as the rep on a rep page. */
  fixed?: Dimension[];
  /** The rows of the download, such as ["rep", "customer"]. */
  download: Dimension[];
  /** A message of the API, such as a wrong filter. */
  message?: string;
  children: ReactNode;
};

export async function SalesFrame({ context, path, tab, back, eyebrow, title, description, fixed = [], download, message, children }: SalesFrameProps) {
  const { base, state, trail, saved, current } = context;
  const fixedAll = [...new Set([...fixed, ...trail.map((step) => step.dimension)])];
  const [selected, channelRows, meta] = await Promise.all([
    selectedLabels(state),
    byDimension({ ...state, include: { ...state.include, channel: [] } }, "channel").catch(() => ({ rows: [] })),
    getMeta().catch(() => ({ data: undefined })),
  ]);
  const dataTo = meta.data?.dataTo;
  const presets = periodPresets().filter((preset) => (prototypePeriods as readonly string[]).includes(preset.id));
  const savedQuery = stateQuery({ ...base, rows: ["rep"], columns: "months", open: [], sort: "value" }, false);
  savedQuery.delete("rows");
  const downloadQuery = stateQuery({ ...state, rows: download, columns: "months", open: [], sort: "value" }, false);

  return (
    <PageFrame>
      {back ? <BackLink href={back.href} label={back.label} /> : null}
      <PageHeader
        eyebrow={eyebrow}
        title={title}
        description={description}
        actions={
          <Stack direction="row" wrap align="end" gap="xs">
            <SavedFilters path={path} filters={saved} current={current} query={savedQuery.toString()} />
            <ButtonLink href={`${basePath}/export?${downloadQuery}`}>Download Excel</ButtonLink>
          </Stack>
        }
      />
      <Stack gap="l">
        {trail.length > 0 ? (
          <FilterChips
            label="Drill-down: the page shows only these. Press one to take it away."
            chips={trail.map((step, index) => ({
              label: `${dimensionLabel(step.dimension)}: ${selected[step.dimension]?.find((item) => item.value === step.key)?.label ?? (step.key || "None")} ✕`,
              href: pageHref(path, base, { trail: trail.filter((_, other) => other !== index), saved: current?.id }),
              current: true,
            }))}
          />
        ) : null}
        {tab ? <TabNav label="Sales report" tabs={tabs.map((item) => ({ label: item.label, href: sectionHref(context, item.path), current: item.label === tab }))} /> : null}
        {context.savedError ? (
          <Notice tone="warning" title="The saved filter did not change" live>
            {context.savedError}
          </Notice>
        ) : null}
        {message ? (
          <Notice tone="warning" title="The filters are not correct">
            {message}
          </Notice>
        ) : null}
        {dataTo !== undefined && isStale(dataTo) ? (
          <Notice tone="warning" title="The sales data is more than a day old">
            {freshness(dataTo)} The nightly load runs at 04:00.
          </Notice>
        ) : null}
        <FilterBar
          path={path}
          state={state}
          selected={selected}
          presets={presets}
          channels={channelRows.rows.map((row) => row.key).sort((a, b) => (a === "Other" ? 1 : b === "Other" ? -1 : a.localeCompare(b)))}
          trail={trail}
          saved={current?.id}
          fixed={fixedAll}
          note={`${periodNote(state.from, state.to)}. Credit notes are subtracted. ${freshness(dataTo)}`}
        />
        {children}
      </Stack>
    </PageFrame>
  );
}
