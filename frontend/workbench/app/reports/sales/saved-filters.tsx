"use client";

import { useRouter } from "next/navigation";
import type { SavedFilter } from "../../../src/api/sales-report";
import { CheckboxField, ConfirmDialog, SelectField, Stack, TextField } from "../../../src/ui";
import { deleteFilterAction, saveFilterAction, updateFilterAction } from "./actions";

type SavedFiltersProps = {
  path: string;
  filters: SavedFilter[];
  /** The saved filter that the page shows, when it shows one. */
  current?: SavedFilter;
  /** The query string of the page now, without the open rows. */
  query: string;
};

/**
 * The named filters: open one, save the page as a new one, and save changes
 * to or delete one of your own.
 */
export function SavedFilters({ path, filters, current, query }: SavedFiltersProps) {
  const router = useRouter();
  const changed = current ? current.query !== query : false;

  return (
    <Stack direction="row" wrap align="end" gap="xs">
      {filters.length > 0 ? (
        <SelectField
          name="savedFilter"
          label="Saved filters"
          showOptional={false}
          placeholder="Open a saved filter"
          options={filters.map((filter) => ({ value: filter.id, label: filter.mine ? filter.name : `${filter.name} (${filter.ownerName})` }))}
          value={current?.id ?? ""}
          onChange={(event) => {
            const filter = filters.find((item) => item.id === event.target.value);
            router.push(filter ? `${path}?${filter.query}&saved=${filter.id}` : path);
          }}
        />
      ) : null}
      {current?.mine && changed ? (
        <ConfirmDialog
          triggerLabel="Save changes"
          triggerVariant="secondary"
          title={`Save the changes to "${current.name}"?`}
          confirmLabel="Save changes"
          pendingLabel="Saving"
          action={updateFilterAction}
          fields={{ id: current.id, query, name: current.name, ...(current.shared ? { shared: "on" } : {}) }}
        >
          <p>The saved filter gets the period, the filters and the drill-down that the page shows now.</p>
        </ConfirmDialog>
      ) : null}
      <ConfirmDialog
        triggerLabel="Save filter as"
        triggerVariant="secondary"
        title="Save this filter"
        confirmLabel="Save filter"
        pendingLabel="Saving"
        action={saveFilterAction}
        fields={{ query }}
      >
        <Stack gap="s">
          <TextField name="name" label="Name" required maxLength={100} placeholder="Gauteng pharmacies, this year" />
          <CheckboxField name="shared" label="Share with the team" hint="Other people can open a shared filter. Only you can change it." />
        </Stack>
      </ConfirmDialog>
      {current?.mine ? (
        <ConfirmDialog
          triggerLabel="Delete"
          triggerVariant="tertiary"
          title={`Delete the saved filter "${current.name}"?`}
          confirmLabel="Delete filter"
          pendingLabel="Deleting"
          tone="danger"
          action={deleteFilterAction}
          fields={{ id: current.id, query }}
        >
          <p>{current.shared ? "Other people also lose this filter. " : ""}You cannot undo this. The report itself does not change.</p>
        </ConfirmDialog>
      ) : null}
    </Stack>
  );
}
