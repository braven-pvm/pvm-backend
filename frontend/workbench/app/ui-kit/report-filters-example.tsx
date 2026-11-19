"use client";

import { useState } from "react";
import { MultiSelect, OrderPicker, Stack, type MultiSelectOption } from "../../src/ui";

// Example data only.
const customers: MultiSelectOption[] = [
  { value: "C1", label: "Example Pharmacy One", detail: "R 12 400" },
  { value: "C2", label: "Example Pharmacy Two", detail: "R 9 870" },
  { value: "C3", label: "Example Store", detail: "R 4 100" },
  { value: "C4", label: "Example Gym", detail: "R 1 050" },
];

async function loadExample(search: string) {
  await new Promise((resolve) => setTimeout(resolve, 300));
  return customers.filter((customer) => customer.label.toLowerCase().includes(search.toLowerCase()));
}

/** The MultiSelect and the OrderPicker with example data, for the UI kit. */
export function ReportFiltersExample() {
  const [selected, setSelected] = useState<string[]>(["C2"]);
  const [order, setOrder] = useState(["rep", "customer", "product"]);
  return (
    <Stack gap="m">
      <MultiSelect
        label="Customers"
        selected={customers.filter((customer) => selected.includes(customer.value))}
        load={loadExample}
        onApply={setSelected}
      />
      <OrderPicker
        label="Drill-down"
        options={[
          { value: "rep", label: "Rep" },
          { value: "customer", label: "Customer" },
          { value: "product", label: "Product" },
          { value: "province", label: "Province" },
        ]}
        value={order}
        max={3}
        onChange={setOrder}
      />
    </Stack>
  );
}
