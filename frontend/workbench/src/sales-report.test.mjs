import assert from "node:assert/strict";
import { test } from "node:test";
import {
  financialYearOf,
  formatFigure,
  readOpen,
  reportQuery,
  toggleOpen,
  withYearTotals,
} from "./sales-report.ts";

test("the financial year starts in March", () => {
  assert.equal(financialYearOf(new Date(Date.UTC(2026, 1, 28))), 2026);
  assert.equal(financialYearOf(new Date(Date.UTC(2026, 2, 1))), 2027);
  assert.equal(financialYearOf(new Date(Date.UTC(2026, 10, 18))), 2027);
});

test("open rows come from a comma list", () => {
  assert.deepEqual(readOpen("R1,R1|RETAIL,,"), ["R1", "R1|RETAIL"]);
  assert.deepEqual(readOpen(undefined), []);
});

test("a closed row opens, and an open row closes with all rows under it", () => {
  assert.deepEqual(toggleOpen(["R1"], "R2"), ["R1", "R2"]);
  assert.deepEqual(toggleOpen(["R1", "R1|RETAIL", "R1|RETAILX", "R2"], "R1|RETAIL"), ["R1", "R1|RETAILX", "R2"]);
  assert.deepEqual(toggleOpen(["R1", "R1|RETAIL", "R2"], "R1"), ["R2"]);
});

test("the report query keeps only the filters in use", () => {
  const query = reportQuery({ years: "2026,2027", channel: "", rep: "R1", measure: "amount", other: "x" });
  assert.equal(query.toString(), "years=2026%2C2027&rep=R1&measure=amount");
});

test("figures are whole numbers with a space between thousands", () => {
  assert.equal(formatFigure(1234567.49), "1 234 567");
  assert.equal(formatFigure(-1500.5), "-1 501");
  assert.equal(formatFigure(0), "0");
  assert.equal(formatFigure(-0.2), "0");
});

test("year totals go after the 12 months of each year", () => {
  const columns = [
    ...Array.from({ length: 12 }, (_, index) => ({ financialYear: 2026, month: index + 1, label: `m${index + 1}` })),
    ...Array.from({ length: 12 }, (_, index) => ({ financialYear: 2027, month: index + 1, label: `n${index + 1}` })),
  ];
  const values = Array.from({ length: 24 }, (_, index) => (index < 12 ? 1 : 2));

  const result = withYearTotals(columns, values);

  assert.equal(result.headers.length, 26);
  assert.equal(result.headers[12], "FY2026");
  assert.equal(result.headers[25], "FY2027");
  assert.equal(result.values[12], 12);
  assert.equal(result.values[25], 24);
  assert.deepEqual(result.yearHeaders, [
    { label: "FY2026", span: 13 },
    { label: "FY2027", span: 13 },
  ]);
});
