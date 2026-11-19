"use client";

import { useRouter } from "next/navigation";
import { useCallback, useState, useTransition } from "react";
import {
  dimensions,
  maxMonths,
  maxRows,
  monthCount,
  selectionCount,
  stateQuery,
  type ColumnMode,
  type Dimension,
  type Measure,
  type ReportState,
} from "../../../src/sales-report";
import { Button, CheckboxField, MultiSelect, OrderPicker, Panel, SelectField, Spinner, Stack, TextField, type MultiSelectOption } from "../../../src/ui";
import { loadValuesAction } from "./actions";
import { reportHref } from "./links";

type Preset = { id: string; label: string; from: string; to: string };

type ReportControlsProps = {
  path: string;
  state: ReportState;
  /** The labels of the selected values, for each dimension. */
  selected: Partial<Record<Dimension, MultiSelectOption[]>>;
  presets: Preset[];
  /** The ID of the saved filter that the page shows. */
  saved?: string;
};

/** One multi-select. Its load function stays the same while the filter stays the same. */
function DimensionFilter({
  dimension,
  label,
  query,
  selected,
  onApply,
}: {
  dimension: Dimension;
  label: string;
  query: string;
  selected: MultiSelectOption[];
  onApply: (values: string[]) => void;
}) {
  const load = useCallback((search: string) => loadValuesAction(dimension, query, search), [dimension, query]);
  return <MultiSelect label={label} selected={selected} load={load} onApply={onApply} />;
}

/**
 * The settings of the sales report: the period, the measure, the columns, a
 * filter for each dimension and the order of the drill-down. Each change goes
 * into the URL, and the server reads the report again.
 */
export function ReportControls({ path, state, selected, presets, saved }: ReportControlsProps) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const preset = presets.find((item) => item.from === state.from && item.to === state.to)?.id ?? "custom";
  const [custom, setCustom] = useState(preset === "custom");
  const [from, setFrom] = useState(state.from);
  const [to, setTo] = useState(state.to);

  const go = (next: Partial<ReportState>, keepOpen = false) => {
    const changed = { ...state, ...next, open: keepOpen ? state.open : [] };
    startTransition(() => router.push(reportHref(path, changed, saved), { scroll: false }));
  };

  // The filter of the other dimensions, for the values that a multi-select offers.
  const query = stateQuery(state, false).toString();
  const months = monthCount(from, to);
  const monthsProblem = months < 1 ? "The first month must be before the last month." : months > maxMonths ? `Select at most ${maxMonths} months.` : undefined;
  const count = selectionCount(state);

  return (
    <Panel>
      <Stack gap="m">
      <Stack direction="row" wrap align="end" gap="s">
        <SelectField
          name="period"
          label="Period"
          showOptional={false}
          options={[...presets.map((item) => ({ value: item.id, label: item.label })), { value: "custom", label: "Custom months" }]}
          value={custom ? "custom" : preset}
          onChange={(event) => {
            const chosen = presets.find((item) => item.id === event.target.value);
            if (chosen) {
              setCustom(false);
              setFrom(chosen.from);
              setTo(chosen.to);
              go({ from: chosen.from, to: chosen.to });
            } else {
              setCustom(true);
            }
          }}
        />
        {custom ? (
          <>
            <TextField name="from" label="From" type="month" showOptional={false} value={from} onChange={(event) => setFrom(event.target.value)} error={monthsProblem} />
            <TextField name="to" label="To" type="month" showOptional={false} value={to} onChange={(event) => setTo(event.target.value)} />
            <Button variant="secondary" disabled={Boolean(monthsProblem) || (from === state.from && to === state.to)} onClick={() => go({ from, to })}>
              Show the months
            </Button>
          </>
        ) : null}
        <SelectField
          name="measure"
          label="Measure"
          showOptional={false}
          options={[
            { value: "amount", label: "Amount (R)" },
            { value: "quantity", label: "Quantity" },
            { value: "customers", label: "Customers" },
          ]}
          value={state.measure}
          onChange={(event) => go({ measure: event.target.value as Measure }, true)}
        />
        <SelectField
          name="columns"
          label="Columns"
          showOptional={false}
          options={[
            { value: "months", label: "Months" },
            { value: "years", label: "Financial years" },
            { value: "none", label: "Total only" },
          ]}
          value={state.columns}
          onChange={(event) => go({ columns: event.target.value as ColumnMode }, true)}
        />
        <SelectField
          name="sort"
          label="Sort"
          showOptional={false}
          options={[
            { value: "value", label: "Largest first" },
            { value: "label", label: "By name" },
          ]}
          value={state.sort}
          onChange={(event) => go({ sort: event.target.value === "label" ? "label" : "value" }, true)}
        />
        <CheckboxField name="compare" label="Compare with a year before" checked={state.compare} onChange={(event) => go({ compare: event.target.checked }, true)} />
        {pending ? <Spinner size="s" label="Updating" /> : null}
      </Stack>

      <Stack direction="row" wrap align="center" gap="xs">
        {dimensions.map((dimension) => (
          <DimensionFilter
            key={dimension.id}
            dimension={dimension.id}
            label={dimension.plural}
            query={query}
            selected={selected[dimension.id] ?? (state.include[dimension.id] ?? []).map((key) => ({ value: key, label: key }))}
            onApply={(values) => go({ include: { ...state.include, [dimension.id]: values } })}
          />
        ))}
        {count > 0 ? (
          <Button variant="tertiary" onClick={() => go({ include: {} })}>
            Clear filters ({count})
          </Button>
        ) : null}
      </Stack>

      <OrderPicker
        label="Drill-down"
        options={dimensions.map((dimension) => ({ value: dimension.id, label: dimension.label }))}
        value={state.rows}
        max={maxRows}
        onChange={(rows) => go({ rows: rows as Dimension[] })}
      />
      </Stack>
    </Panel>
  );
}
