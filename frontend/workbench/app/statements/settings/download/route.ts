import { getStatementSettings } from "../../../../src/api/statements";
import { requireWorkbenchUser } from "../../../../src/auth/session";

/** Gives the settings of one company as a .json file, for a person to change and upload again. */
export async function GET(request: Request) {
  await requireWorkbenchUser("/statements/settings");
  const company = new URL(request.url).searchParams.get("company");
  if (!company) {
    return new Response("Choose a company.", { status: 400 });
  }

  const settings = await getStatementSettings(company);
  if (!settings) {
    return new Response(`There are no settings for ${company}.`, { status: 404 });
  }

  const name = company.replace(/[^A-Za-z0-9 &-]/g, "").trim() || "company";
  return new Response(JSON.stringify(settings, null, 2), {
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Content-Disposition": `attachment; filename="${name} settings.json"`,
    },
  });
}
