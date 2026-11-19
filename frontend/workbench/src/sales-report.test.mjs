import assert from "node:assert/strict";
import test from "node:test";
import {
  addMonths,
  apiQuery,
  financialYearOf,
  focusRow,
  formatChange,
  formatFigure,
  monthCount,
  monthLabel,
  periodPresets,
  readState,
  selectionCount,
  shortFigure,
  stateQuery,
  toggleOpen,
} from "./sales-report.ts";

const today = new Date(Date.UTC(2026, 10, 19)); // 19 November 2026: financial year 2027.

test("the financial year runs from March to February", () => {
  assert.equal(financialYearOf(new Date(Date.UTC(2026, 2, 1))), 2027);
  assert.equal(financialYearOf(new Date(Date.UTC(2026, 1, 28))), 2026);
});

test("months add across years and count both ends", () => {
  assert.equal(addMonths("2026-11", 3), "2027-02");
  assert.equal(addMonths("2026-01", -1), "2025-12");
  assert.equal(monthCount("2026-03", "2027-02"), 12);
  assert.equal(monthLabel("2026-03"), "Mar 2026");
});

test("with no parameters the state is the financial year to date by rep, customer and product", () => {
  const state = readState({}, today);
  assert.deepEqual(state, {
    rows: ["rep", "customer", "product"],
    from: "2026-03",
    to: "2026-11",
    measure: "amount",
    columns: "months",
    compare: true,
    sort: "value",
    open: [],
    include: {},
  });
});

test("the state reads rows, months, selections and options, and drops wrong values", () => {
  const state = readState(
    { rows: "product,rep,town,rep", from: "2025-03", to: "2026-02", rep: ["R1,R2", "R3"], customer: "C1", measure: "customers", columns: "years", compare: "0", sort: "label", open: "BAR01,BAR01|R1" },
    today,
  );
  assert.deepEqual(state.rows, ["product", "rep"]);
  assert.equal(state.from, "2025-03");
  assert.deepEqual(state.include, { rep: ["R1", "R2", "R3"], customer: ["C1"] });
  assert.equal(state.measure, "customers");
  assert.equal(state.columns, "years");
  assert.equal(state.compare, false);
  assert.equal(state.sort, "label");
  assert.deepEqual(state.open, ["BAR01", "BAR01|R1"]);
  assert.equal(selectionCount(state), 4);
});

test("months in the wrong order or more than 36 months fall back to the year to date", () => {
  assert.equal(readState({ from: "2026-05", to: "2026-03" }, today).from, "2026-03");
  assert.equal(readState({ from: "2022-03", to: "2026-03" }, today).to, "2026-11");
});

test("the query string leaves defaults out and keeps a fixed order", () => {
  const state = readState({ rep: "R1", rows: "customer", from: "2026-03", to: "2026-11", open: "C1" }, today);
  assert.equal(stateQuery(state).toString(), "rows=customer&from=2026-03&to=2026-11&rep=R1&open=C1");
  assert.equal(stateQuery(state, false).toString(), "rows=customer&from=2026-03&to=2026-11&rep=R1");
  assert.equal(apiQuery(state).get("compare"), "1");
});

test("a row toggles open, and closing it closes the rows under it", () => {
  assert.deepEqual(toggleOpen(["R1"], "R2"), ["R1", "R2"]);
  assert.deepEqual(toggleOpen(["R1", "R1|C1", "R2"], "R1"), ["R2"]);
});

test("a focused row filters its path and the next dimensions become the rows", () => {
  const state = readState({ rows: "rep,customer,product", open: "R1" }, today);
  const rep = focusRow(state, "R1", 0);
  assert.deepEqual(rep.include, { rep: ["R1"] });
  assert.deepEqual(rep.rows, ["customer", "product"]);
  assert.deepEqual(rep.open, []);

  const product = focusRow(state, "R1|C1|BAR01", 2);
  assert.deepEqual(product.include, { rep: ["R1"], customer: ["C1"], product: ["BAR01"] });
  assert.deepEqual(product.rows, ["product"]);
});

test("figures round half away from zero with a space between thousands", () => {
  assert.equal(formatFigure(1234567.5), "1 234 568");
  assert.equal(formatFigure(-1500.5), "-1 501");
  assert.equal(formatFigure(0.4), "0");
});

test("the change against the year before", () => {
  assert.deepEqual(formatChange(110, 100), { text: "+10.0%", tone: "up" });
  assert.deepEqual(formatChange(90, 100), { text: "-10.0%", tone: "down" });
  assert.deepEqual(formatChange(50, 0), { text: "New", tone: "new" });
  assert.deepEqual(formatChange(0, 0), { text: "–", tone: "flat" });
});

test("short figures for the chart axis", () => {
  assert.equal(shortFigure(1_250_000, "amount"), "R 1.3 m");
  assert.equal(shortFigure(850_400, "quantity"), "850 k");
});

test("the period presets start at the financial year", () => {
  const presets = periodPresets(today);
  assert.deepEqual(presets[0], { id: "fytd", label: "FY2027 to date", from: "2026-03", to: "2026-11" });
  assert.deepEqual(presets[1], { id: "lastfy", label: "FY2026", from: "2025-03", to: "2026-02" });
  assert.deepEqual(presets[2].from, "2025-12");
});
