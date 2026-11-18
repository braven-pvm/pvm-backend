using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Pvm.Api.Auth;
using Pvm.Api.Features.Reports;

namespace Pvm.Api.Tests.Reports;

public sealed class SellOutEndpointsTests
{
    private static IFormFile Upload(byte[] content, string name = "Makro Dispo Report PVM 05112026.xls") =>
        new FormFile(new MemoryStream(content), 0, content.Length, "file", name);

    [Fact]
    public async Task An_upload_becomes_a_file_with_its_name_and_the_south_african_date()
    {
        // 23:30 UTC is already the next day in South Africa.
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 17, 23, 30, 0, TimeSpan.Zero));

        var (file, problem) = await SellOutEndpoints.ReadFileAsync(Upload([1, 2, 3], @"C:\temp\report.xls"), clock, CancellationToken.None);

        Assert.Null(problem);
        Assert.Equal("report.xls", file!.FileName);
        Assert.Equal([1, 2, 3], file.Content);
        Assert.Equal(new DateOnly(2026, 11, 18), file.ReceivedOn);
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        var (file, problem) = await SellOutEndpoints.ReadFileAsync(Upload([]), TimeProvider.System, CancellationToken.None);

        Assert.Null(file);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(problem).StatusCode);
    }

    [Fact]
    public async Task A_too_large_upload_is_refused_before_it_is_read()
    {
        var large = new FormFile(Stream.Null, 0, SellOutEndpoints.MaxFileBytes + 1, "file", "big.xlsx");

        var (file, problem) = await SellOutEndpoints.ReadFileAsync(large, TimeProvider.System, CancellationToken.None);

        Assert.Null(file);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(problem).StatusCode);
    }

    [Theory]
    [InlineData("makro-dispo", "100000001", "ITEM01", null, null)]
    [InlineData("makro-dispo", "100000001", "ITEM01", "20", null)]
    [InlineData("", "100000001", "ITEM01", null, "Give the source and the product code.")]
    [InlineData("makro-dispo", "100000001", " ", null, "Give the Acumatica item.")]
    [InlineData("makro-dispo", "100000001", "ITEM01", "0", "Units per item must be more than 0 and at most 10000.")]
    public void A_product_map_request_is_checked(string source, string code, string item, string? units, string? expected)
    {
        var request = new ProductMapRequest(source, code, item, units is null ? null : decimal.Parse(units, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected, SellOutEndpoints.Validate(request));
    }

    [Fact]
    public void The_import_names_the_user_who_uploaded_it()
    {
        var accessor = new CurrentAppUserAccessor { User = new CurrentAppUser(Guid.NewGuid(), null, "person@example.com", "Person", ["Admin"]) };

        Assert.Equal("upload:person@example.com", SellOutEndpoints.ReceivedBy(accessor));
        Assert.Equal("upload:unknown", SellOutEndpoints.ReceivedBy(new CurrentAppUserAccessor()));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
