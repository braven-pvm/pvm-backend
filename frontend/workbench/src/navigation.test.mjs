import assert from "node:assert/strict";
import { test } from "node:test";
import { isCurrent, navigationFor } from "./navigation.ts";

const labels = (groups) => groups.flatMap((group) => group.items.map((item) => item.label));

test("a viewer sees no write or admin items", () => {
  const groups = navigationFor(["Viewer"]);
  assert.ok(!labels(groups).includes("Banking"));
  assert.ok(!labels(groups).includes("Users"));
});

test("an operator sees banking but not admin settings", () => {
  const groups = navigationFor(["Operator"]);
  assert.ok(labels(groups).includes("Banking"));
  assert.ok(!labels(groups).includes("Users"));
});

test("an admin sees everything", () => {
  const groups = navigationFor(["Admin"]);
  for (const label of ["Today", "Exceptions", "Shoprite", "Banking", "Automations", "Runs", "Events", "Users"]) {
    assert.ok(labels(groups).includes(label), label);
  }
});

test("a group with nothing allowed disappears", () => {
  const groups = navigationFor([]);
  assert.ok(!groups.some((group) => group.label === "Settings"));
});

test("home is current only on the home page", () => {
  assert.equal(isCurrent({ href: "/" }, "/"), true);
  assert.equal(isCurrent({ href: "/" }, "/shoprite/invoices"), false);
});

test("a section matches its own pages and its extra prefixes", () => {
  const shoprite = { href: "/shoprite/invoices", matches: ["/shoprite/invoices", "/shoprite/purchase-orders"] };
  assert.equal(isCurrent(shoprite, "/shoprite/invoices/abc"), true);
  assert.equal(isCurrent(shoprite, "/shoprite/purchase-orders"), true);
  assert.equal(isCurrent(shoprite, "/shoprite/invoices-archive"), false);
  assert.equal(isCurrent(shoprite, "/banking"), false);
});

test("the Shoprite entry is current on every page of the module", () => {
  const shoprite = navigationFor(["Admin"])
    .flatMap((group) => group.items)
    .find((item) => item.label === "Shoprite");
  assert.equal(shoprite.href, "/shoprite");
  assert.equal(isCurrent(shoprite, "/shoprite/invoices/abc"), true);
  assert.equal(isCurrent(shoprite, "/shoprite/settings/inventory-mappings"), true);
  assert.equal(isCurrent(shoprite, "/banking"), false);
});
