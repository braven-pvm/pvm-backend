import { CheckboxField, CheckboxGroup } from "../../../src/ui";

export const roles = [
  { value: "Viewer", hint: "Sees everything. Changes nothing." },
  { value: "Operator", hint: "Imports statements, reads invoices and submits them to Shoprite." },
  { value: "Admin", hint: "Everything, plus users, mappings, exceptions and automation policy." },
];

/** One checkbox per role. A user can have more than one role. */
export function RolesField({ selected = [] }: { selected?: string[] }) {
  return (
    <CheckboxGroup label="Roles" hint="A user can have more than one role">
      {roles.map((role) => (
        <CheckboxField
          key={role.value}
          id={`role-${role.value}`}
          name="roles"
          value={role.value}
          label={role.value}
          hint={role.hint}
          defaultChecked={selected.includes(role.value)}
        />
      ))}
    </CheckboxGroup>
  );
}
