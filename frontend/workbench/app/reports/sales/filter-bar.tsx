"use client";

import { useRouter } from "next/navigation";
import { useCallback, useState, useTransition } from "react";
import {
  dimensions,
  maxMonths,
  monthCount,
  selectionCount,
  stateQuery,
  type Dimension,
  type ReportState,
  type TrailStep,
} from "../../../src/sales-report";
import { Button, FilterChips, MultiSelect, Panel, SelectField, Spinner, Stack, TextField, type MultiSelectOption } from "../../../src/ui";
import { loadValuesAction } from "./actions";
import { pageHref } from "./links";

type Preset = { id: string; label: string; from: string; to: string };

type FilterBarProps = {
  path: string;
  state: ReportState;
  /** The labels of the selected values, for each dimension. */
  selected: Partial<Record<Dimension, MultiSelectOption[]>>;
  presets: Preset[];
  /** The channels for the chips. */
  channels: string[];
  trail: TrailStep[];
  saved?: string;
  /** The dimensions that the page fixes, such as the rep on a rep page. They do not show. */
  fixed?: Dimension[];
  /** The line under the bar: the months, the comparison and the freshness of the data. */
  note: string;
};

const main: Dimension[] = ["rep", "itemGroup"];
const more: Dimension[] = ["customer", "customerGroup", "province", "product", "warehouse", "repAtSale"];

function DimensionFilter({ dimension, query, selected, onApply }: { dimension: Dimension; query: string; selected: MultiSelectOption[]; onApply: (values: string[]) => void }) {
  const load = useCallback((search: string) => loadValuesAction(dimension, query, search), [dimension, query]);
  const label = dimension === "itemGroup" ? "Product groups" : (dimensions.find((item) => item.id === dimension)?.plural ?? dimension);
  return <MultiSelect label={label} selected={selected} load={load} onApply={onApply} />;
}

/**
 * The filter bar of the prototype: the period, the reps, the product groups and the channel
 * chips. Each filter takes many values. "More filters" opens the other dimensions.
 */
export function FilterBar({ path, state, selected, presets, channels, trail, saved, fixed = [], note }: FilterBarProps) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const preset = presets.find((item) => item.from === state.from && item.to === state.to)?.id ?? "custom";
  const [custom, setCustom] = useState(preset === "custom");
  const [from, setFrom] = useState(state.from);
  const [to, setTo] = useState(state.to);
  const moreCount = more.reduce((sum, dimension) => sum + (fixed.includes(dimension) ? 0 : (state.include[dimension]?.length ?? 0)), 0);
  const [showMore, setShowMore] = useState(moreCount > 0);

  const href = (next: ReportState) => pageHref(path, next, { trail, saved });
  const go = (next: Partial<ReportState>) => startTransition(() => router.push(href({ ...state, ...next }), { scroll: false }));
  const query = stateQuery(state, false).toString();
  const labelsOf = (dimension: Dimension) => selected[dimension] ?? (state.include[dimension] ?? []).map((key) => ({ value: key, label: key }));
  const apply = (dimension: Dimension) => (values: string[]) => go({ include: { ...state.include, [dimension]: values } });

  const months = monthCount(from, to);
  const monthsProblem = months < 1 ? "The first month must be before the last month." : months > maxMonths ? `Select at most ${maxMonths} months.` : undefined;
  const chosenChannels = state.include.channel ?? [];
  const toggleChannel = (channel: string) =>
    href({ ...state, include: { ...state.include, channel: chosenChannels.includes(channel) ? chosenChannels.filter((item) => item !== channel) : [...chosenChannels, channel] } });
  const fixedSet = new Set(fixed);
  const clearable = selectionCount({ ...state, include: Object.fromEntries(Object.entries(state.include).filter(([dimension]) => !fixedSet.has(dimension as Dimension))) });

  return (
    <Panel>
      <Stack gap="s">
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
              <Button disabled={Boolean(monthsProblem) || (from === state.from && to === state.to)} onClick={() => go({ from, to })}>
                Show the months
              </Button>
            </>
          ) : null}
          {main
            .filter((dimension) => !fixedSet.has(dimension))
            .map((dimension) => (
              <DimensionFilter key={dimension} dimension={dimension} query={query} selected={labelsOf(dimension)} onApply={apply(dimension)} />
            ))}
          <Button variant="tertiary" aria-expanded={showMore} onClick={() => setShowMore(!showMore)}>
            {showMore ? "Fewer filters" : `More filters${moreCount > 0 ? ` (${moreCount})` : ""}`}
          </Button>
          {clearable > 0 ? (
            <Button variant="tertiary" onClick={() => go({ include: Object.fromEntries(Object.entries(state.include).filter(([dimension]) => fixedSet.has(dimension as Dimension))) })}>
              Clear filters ({clearable})
            </Button>
          ) : null}
          {pending ? <Spinner size="s" label="Updating" /> : null}
        </Stack>
        {fixedSet.has("channel") || channels.length < 2 ? null : (
          <FilterChips
            label="Channels"
            chips={[
              { label: "All channels", href: href({ ...state, include: { ...state.include, channel: [] } }), current: chosenChannels.length === 0 },
              ...channels.map((channel) => ({ label: channel, href: toggleChannel(channel), current: chosenChannels.includes(channel) })),
            ]}
          />
        )}
        {showMore ? (
          <Stack direction="row" wrap align="center" gap="xs">
            {more
              .filter((dimension) => !fixedSet.has(dimension))
              .map((dimension) => (
                <DimensionFilter key={dimension} dimension={dimension} query={query} selected={labelsOf(dimension)} onApply={apply(dimension)} />
              ))}
          </Stack>
        ) : null}
        <small>{note}</small>
      </Stack>
    </Panel>
  );
}
