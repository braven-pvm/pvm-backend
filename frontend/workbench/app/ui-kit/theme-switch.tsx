"use client";

import { useState } from "react";
import { Moon, Sun } from "lucide-react";
import { Button } from "../../src/ui";

/**
 * Switches the whole page between the light and dark tokens, so each
 * component can be checked in both themes. Development only, until step B2.
 */
export function ThemeSwitch() {
  const [dark, setDark] = useState(false);
  return (
    <Button
      variant="secondary"
      aria-pressed={dark}
      onClick={() => {
        const next = !dark;
        document.documentElement.dataset.theme = next ? "dark" : "light";
        setDark(next);
      }}
    >
      {dark ? <Sun aria-hidden="true" /> : <Moon aria-hidden="true" />}
      {dark ? "Show light theme" : "Show dark theme"}
    </Button>
  );
}
