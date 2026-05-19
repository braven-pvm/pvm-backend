export type AuthMode = "Entra" | "DevelopmentBypass";

export function getAuthMode(): AuthMode {
  const mode = process.env.AUTH_MODE ?? "Entra";
  if (mode === "DevelopmentBypass") {
    if (process.env.NODE_ENV !== "development") {
      throw new Error("AUTH_MODE=DevelopmentBypass is only allowed in development.");
    }

    return "DevelopmentBypass";
  }

  return "Entra";
}

export function isDevelopmentBypass() {
  return getAuthMode() === "DevelopmentBypass";
}
