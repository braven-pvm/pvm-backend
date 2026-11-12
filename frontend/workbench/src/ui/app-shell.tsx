"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState, type ReactNode } from "react";
import {
  Activity,
  History,
  House,
  Inbox,
  Landmark,
  LogOut,
  Menu,
  Package,
  Store,
  TriangleAlert,
  Users,
  X,
  Zap,
  type LucideIcon,
} from "lucide-react";
import { isCurrent, type NavGroup, type NavIcon, type NavItem } from "../navigation";
import styles from "./app-shell.module.css";

const icons: Record<NavIcon, LucideIcon> = {
  today: House,
  exceptions: Inbox,
  shoprite: Store,
  banking: Landmark,
  stock: Package,
  automations: Zap,
  runs: History,
  events: Activity,
  users: Users,
};

/** Pages that render without the shell: there is no signed-in user to navigate for. */
const bareRoutes = ["/sign-in", "/access-denied"];

export type EnvironmentKind = "production" | "qa" | "development";

type AppShellProps = {
  groups: NavGroup[];
  environment: EnvironmentKind;
  userName?: string;
  canSignOut: boolean;
  children: ReactNode;
};

export function AppShell({ groups, environment, userName, canSignOut, children }: AppShellProps) {
  const pathname = usePathname();
  // The mobile menu stays open only on the page it was opened on, so navigating closes it.
  const [menuOpenOn, setMenuOpenOn] = useState<string | null>(null);
  const menuOpen = menuOpenOn === pathname;

  if (bareRoutes.some((route) => pathname === route || pathname.startsWith(`${route}/`))) {
    return <>{children}</>;
  }

  return (
    <div className={styles.shell}>
      <aside className={styles.sidebar} data-open={menuOpen}>
        <div className={styles.sidebarTop}>
          <Link className={styles.brand} href="/">
            <span className={styles.brandMark} aria-hidden="true">P</span>
            PVM Backoffice
          </Link>
          <button
            type="button"
            className={styles.menuButton}
            aria-expanded={menuOpen}
            aria-controls="main-navigation"
            aria-label={menuOpen ? "Close menu" : "Menu"}
            onClick={() => setMenuOpenOn(menuOpen ? null : pathname)}
          >
            {menuOpen ? <X size={20} aria-hidden="true" /> : <Menu size={20} aria-hidden="true" />}
            <span>{menuOpen ? "Close menu" : "Menu"}</span>
          </button>
        </div>

        <nav id="main-navigation" className={styles.nav} aria-label="Main navigation">
          {groups.map((group, index) => (
            <div className={styles.group} key={group.label ?? `group-${index}`}>
              {group.label ? <p className={styles.groupLabel}>{group.label}</p> : null}
              <ul className={styles.list}>
                {group.items.map((item) => (
                  <NavEntry key={item.href} item={item} pathname={pathname} />
                ))}
              </ul>
            </div>
          ))}
        </nav>

        <div className={styles.account}>
          {userName ? <span className={styles.userName}>{userName}</span> : null}
          {canSignOut ? (
            <Link className={styles.signOut} href="/api/auth/signout">
              <LogOut size={16} aria-hidden="true" />
              Sign out
            </Link>
          ) : null}
        </div>
      </aside>

      <div className={styles.main}>
        <EnvironmentBand environment={environment} />
        {children}
      </div>
    </div>
  );
}

function NavEntry({ item, pathname }: { item: NavItem; pathname: string }) {
  const Icon = item.icon ? icons[item.icon] : null;
  const inSection = isCurrent(item, pathname);
  const children = item.children ?? [];
  const currentChild = children.find((child) => isCurrent(child, pathname));
  // The item itself is the current page only when no child is a closer match.
  const isPage = inSection && !currentChild;

  return (
    <li>
      <Link
        className={styles.link}
        href={item.href}
        data-section={inSection}
        aria-current={isPage ? "page" : undefined}
      >
        {Icon ? <Icon size={18} aria-hidden="true" /> : null}
        {item.label}
      </Link>
      {inSection && children.length > 0 ? (
        <ul className={styles.subList}>
          {children.map((child) => (
            <li key={child.href}>
              <Link
                className={styles.subLink}
                href={child.href}
                aria-current={child === currentChild ? "page" : undefined}
              >
                {child.label}
              </Link>
            </li>
          ))}
        </ul>
      ) : null}
    </li>
  );
}

function EnvironmentBand({ environment }: { environment: EnvironmentKind }) {
  if (environment === "development") {
    return null;
  }

  const production = environment === "production";
  return (
    <div className={styles.band} data-environment={environment} role="note">
      <TriangleAlert size={16} aria-hidden="true" />
      {production
        ? "Production — changes affect live Acumatica, bank and Shoprite data"
        : "QA environment — test data only"}
    </div>
  );
}
