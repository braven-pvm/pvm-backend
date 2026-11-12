/**
 * The sidebar, grouped by intent. Add a new business area under "Operations",
 * never as a new group. An item is shown only when the user holds one of its
 * roles; items with no roles are shown to everyone signed in. Do not list a
 * route until its page exists.
 */
export type NavIcon =
  | "today"
  | "exceptions"
  | "shoprite"
  | "banking"
  | "stock"
  | "automations"
  | "runs"
  | "events"
  | "users";

export type NavItem = {
  label: string;
  href: string;
  icon?: NavIcon;
  roles?: string[];
  /** Other path prefixes that mark this item as current. */
  matches?: string[];
  children?: NavItem[];
};

export type NavGroup = {
  label?: string;
  items: NavItem[];
};

const write = ["Admin", "Operator"];
const admin = ["Admin"];

export const navigation: NavGroup[] = [
  {
    items: [
      { label: "Today", href: "/", icon: "today" },
      { label: "Exceptions", href: "/exceptions", icon: "exceptions" },
    ],
  },
  {
    label: "Operations",
    items: [
      {
        label: "Shoprite",
        href: "/shoprite",
        icon: "shoprite",
        // The module's sections are tabs on its pages, not sidebar children.
      },
      { label: "Banking", href: "/banking", icon: "banking", roles: write },
      { label: "Stock", href: "/stock", icon: "stock" },
    ],
  },
  {
    label: "Automation",
    items: [
      { label: "Automations", href: "/automations", icon: "automations" },
      { label: "Runs", href: "/runs", icon: "runs" },
      {
        label: "Events",
        href: "/events",
        icon: "events",
        roles: admin,
      },
    ],
  },
  {
    label: "Settings",
    items: [{ label: "Users", href: "/admin/users", icon: "users", roles: admin }],
  },
];

/** The navigation this user may see. With no user, only unrestricted items. */
export function navigationFor(roles: string[]): NavGroup[] {
  const allowed = (item: NavItem) =>
    !item.roles || item.roles.some((role) => roles.includes(role));

  return navigation
    .map((group) => ({
      ...group,
      items: group.items.filter(allowed).map((item) => ({
        ...item,
        children: item.children?.filter(allowed),
      })),
    }))
    .filter((group) => group.items.length > 0);
}

/** True when `pathname` belongs to `item`. "/" matches only itself. */
export function isCurrent(item: Pick<NavItem, "href" | "matches">, pathname: string) {
  const prefixes = item.matches ?? [item.href];
  return prefixes.some((prefix) =>
    prefix === "/" ? pathname === "/" : pathname === prefix || pathname.startsWith(`${prefix}/`),
  );
}
