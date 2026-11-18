import { redirect } from "next/navigation";
import { getStatementCompanies, getStatementSettings, type MerchandiserSettings } from "../../../src/api/statements";
import { hasAnyRole, requireWorkbenchUser } from "../../../src/auth/session";
import { BackLink, ButtonLink, EmptyState, FormActions, KeyValueList, Notice, PageFrame, PageHeader, Panel, Stack } from "../../../src/ui";
import { SettingsUpload } from "./settings-upload";

export const dynamic = "force-dynamic";
export const metadata = { title: "Statement settings" };

/** The settings of each merchandiser company: what each statement takes. An Admin uploads changes. */
export default async function StatementSettingsPage() {
  const user = await requireWorkbenchUser("/statements/settings");
  if (!hasAnyRole(user, ["Admin", "Operator"])) {
    redirect("/");
  }

  let settings: MerchandiserSettings[] = [];
  let failed = false;
  try {
    const names = [...new Set((await getStatementCompanies()).map((company) => company.name))];
    settings = (await Promise.all(names.map((name) => getStatementSettings(name)))).filter((item): item is MerchandiserSettings => item !== null);
  } catch {
    failed = true;
  }

  return (
    <PageFrame>
      <BackLink href="/statements" label="Statements" />
      <PageHeader
        eyebrow="Reports"
        title="Statement settings"
        description="What each merchandiser statement takes: its areas, the customer group and reps of each line, its Makro stores, and its commission or retainer."
      />
      <Stack gap="m">
        {failed ? (
          <Notice tone="error" title="The settings could not be read">
            Try again in a few minutes.
          </Notice>
        ) : null}
        {!failed && settings.length === 0 ? <EmptyState title="No settings yet" description="An Admin uploads the settings file of each company below." /> : null}

        {settings.map((company) => (
          <Panel
            key={company.name}
            title={company.name}
            description={`From ${company.validFrom} · ${company.payment === "retainer" ? `retainer R ${company.retainerAmount}` : `commission ${company.ratePercent}%`}${company.disChemSheet ? " · with the Dis-Chem sheet" : ""}`}
          >
            <Stack gap="m">
              {company.areas.map((area) => (
                <Stack gap="s" key={area.name}>
                  <strong>
                    {area.name}
                    {area.commissionRatePercent != null ? ` · commission ${area.commissionRatePercent}%` : ""}
                  </strong>
                  <KeyValueList
                    items={[
                      ...area.lines.map((line) => ({
                        label: line.label,
                        value: [
                          line.customerGroup,
                          line.reps.length > 0 ? `reps: ${line.reps.join(", ")}` : "all reps",
                          line.customers.length > 0 ? `only customers ${line.customers.join(", ")}` : null,
                          line.disChem ? "Dis-Chem sheet" : null,
                        ]
                          .filter(Boolean)
                          .join(" · "),
                      })),
                      ...(area.makroStores.length > 0 ? [{ label: "Makro", value: area.makroStores.join(", ") }] : []),
                    ]}
                  />
                </Stack>
              ))}
              <FormActions>
                <ButtonLink href={`/statements/settings/download?${new URLSearchParams({ company: company.name })}`} variant="secondary">
                  Download the settings file
                </ButtonLink>
              </FormActions>
            </Stack>
          </Panel>
        ))}

        {hasAnyRole(user, ["Admin"]) ? (
          <Panel title="Upload a settings file" description="To change a company: download its file, change it, and upload it. To start a new version from a later month, change validFrom. Names of groups, reps and Makro stores must be as in Acumatica.">
            <SettingsUpload />
          </Panel>
        ) : null}
      </Stack>
    </PageFrame>
  );
}
