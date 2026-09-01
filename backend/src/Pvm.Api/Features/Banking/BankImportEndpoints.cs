using Pvm.Infrastructure.Investec;
using Pvm.Infrastructure.Nedbank;

namespace Pvm.Api.Features.Banking;

/// <summary>
/// Bank-statement import endpoints:
/// <list type="bullet">
///   <item><c>POST /api/banking/import/nedbank</c> — upload a Nedbank OFX file; it is parsed,
///   renumbered, and pushed into Acumatica (PVMBankFeed).</item>
///   <item><c>POST /api/banking/investec/refresh</c> — pull Investec transactions for a date
///   window and import them into Acumatica.</item>
///   <item><c>GET /api/banking/import</c> — a minimal drag-and-drop upload page.</item>
/// </list>
/// </summary>
public static class BankImportEndpoints
{
    public static IEndpointRouteBuilder MapBankImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/banking");

        group.MapGet("/import", () => Results.Content(UploadPageHtml, "text/html"))
            .RequireAuthorization("Invoices.Write");

        group.MapPost("/import/nedbank", ImportNedbankAsync)
            .RequireAuthorization("Invoices.Write")
            .DisableAntiforgery();

        group.MapPost("/investec/refresh", RefreshInvestecAsync)
            .RequireAuthorization("Invoices.Write");

        return app;
    }

    private static async Task<IResult> ImportNedbankAsync(
        IFormFile file,
        NedbankStatementImportService importer,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return Results.BadRequest(new { message = "No file uploaded, or the file is empty." });
        }

        string ofx;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            ofx = await reader.ReadToEndAsync(cancellationToken);
        }

        try
        {
            var result = await importer.ImportAsync(ofx, cancellationToken);
            return Results.Ok(new NedbankImportResponse(
                file.FileName,
                result.LinesImported,
                result.StatementReference));
        }
        catch (FormatException exception)
        {
            return Results.BadRequest(new { file = file.FileName, message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            // Configuration or Acumatica-side failure (e.g. missing CashAccount / endpoint).
            return Results.Problem(exception.Message);
        }
    }

    private static async Task<IResult> RefreshInvestecAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        InvestecBankFeedRefreshService refreshService,
        CancellationToken cancellationToken)
    {
        var to = toDate ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var from = fromDate ?? to.AddDays(-30);
        if (from > to)
        {
            return Results.BadRequest(new { message = "fromDate must be on or before toDate." });
        }

        try
        {
            var result = await refreshService.RefreshAsync(from, to, cancellationToken);
            return Results.Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(exception.Message);
        }
    }

    private const string UploadPageHtml =
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Bank statement import</title>
          <style>
            body { font-family: system-ui, sans-serif; max-width: 640px; margin: 3rem auto; padding: 0 1rem; color: #1a1a1a; }
            h1 { font-size: 1.3rem; }
            #drop { border: 2px dashed #9aa4b2; border-radius: 10px; padding: 2.5rem 1rem; text-align: center; color: #4b5563; cursor: pointer; }
            #drop.over { border-color: #2563eb; background: #eff6ff; color: #1d4ed8; }
            button { background: #2563eb; color: #fff; border: 0; border-radius: 6px; padding: .6rem 1.1rem; font-size: 1rem; cursor: pointer; }
            button:disabled { opacity: .5; cursor: default; }
            pre { background: #0f172a; color: #e2e8f0; padding: 1rem; border-radius: 8px; overflow-x: auto; white-space: pre-wrap; }
            .ok { color: #15803d; } .err { color: #b91c1c; }
          </style>
        </head>
        <body>
          <h1>Nedbank statement import</h1>
          <p>Download the statement from Netbank as <strong>OFX</strong>, then drop it here. It is cleaned, renumbered, and imported into Acumatica.</p>
          <div id="drop">Drag an OFX file here, or click to choose</div>
          <input id="file" type="file" accept=".ofx,application/x-ofx,text/plain" hidden>
          <p><button id="go" disabled>Import</button> <span id="name"></span></p>
          <pre id="out" hidden></pre>
          <script>
            const drop = document.getElementById('drop'), input = document.getElementById('file'),
                  go = document.getElementById('go'), name = document.getElementById('name'), out = document.getElementById('out');
            let chosen = null;
            function pick(f) { chosen = f; name.textContent = f ? f.name : ''; go.disabled = !f; }
            drop.addEventListener('click', () => input.click());
            input.addEventListener('change', () => pick(input.files[0]));
            ['dragover','dragenter'].forEach(e => drop.addEventListener(e, ev => { ev.preventDefault(); drop.classList.add('over'); }));
            ['dragleave','drop'].forEach(e => drop.addEventListener(e, ev => { ev.preventDefault(); drop.classList.remove('over'); }));
            drop.addEventListener('drop', ev => pick(ev.dataTransfer.files[0]));
            go.addEventListener('click', async () => {
              if (!chosen) return;
              go.disabled = true; out.hidden = false; out.className = ''; out.textContent = 'Importing ' + chosen.name + ' ...';
              const body = new FormData(); body.append('file', chosen);
              try {
                const res = await fetch('/api/banking/import/nedbank', { method: 'POST', body });
                const text = await res.text();
                out.className = res.ok ? 'ok' : 'err';
                out.textContent = (res.ok ? 'Imported OK\n' : 'Failed (' + res.status + ')\n') + text;
              } catch (e) {
                out.className = 'err'; out.textContent = 'Request failed: ' + e;
              } finally {
                go.disabled = false;
              }
            });
          </script>
        </body>
        </html>
        """;
}

/// <summary>Response for a Nedbank OFX import.</summary>
public sealed record NedbankImportResponse(string FileName, int LinesImported, string? StatementReference);
