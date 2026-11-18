using System.Text.Json;
using Pvm.Application.Documents;
using Xunit;

namespace Pvm.Application.Tests.Documents;

public class GeneralDocumentTypeTests
{
    [Fact]
    public void Code_name_and_schema()
    {
        var type = new GeneralDocumentType();

        Assert.Equal("GENERAL", type.Code);
        Assert.Equal("General document", type.DisplayName);
        Assert.Equal(string.Empty, type.AcumaticaEntity);
        var schema = type.FieldSchema;
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["autoTitle", "summary", "documentDate", "searchWords"],
            schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("80 characters", type.ExtractionInstruction);
    }

    [Fact]
    public void Read_takes_the_fields_ignoring_case()
    {
        var read = GeneralDocumentFields.Read(Json("""
            { "AutoTitle": " Eskom statement ", "summary": "Electricity for October.", "documentDate": "2026-10-31", "searchWords": ["Eskom", "eskom", " 4410 ", ""] }
            """));

        Assert.Equal("Eskom statement", read.AutoTitle);
        Assert.Equal("Electricity for October.", read.Summary);
        Assert.Equal(new DateOnly(2026, 10, 31), read.DocumentDate);
        Assert.Equal(["Eskom", "4410"], read.SearchWords);
        Assert.False(read.IsEmpty);
    }

    [Fact]
    public void Read_cuts_to_the_limits()
    {
        var words = string.Join(",", Enumerable.Range(1, 14).Select(index => $"\"word{index}\""));
        var read = GeneralDocumentFields.Read(Json($$"""
            { "autoTitle": "{{new string('t', 100)}}", "summary": "{{new string('s', 700)}}", "documentDate": null, "searchWords": [{{words}}] }
            """));

        Assert.Equal(80, read.AutoTitle!.Length);
        Assert.Equal(500, read.Summary!.Length);
        Assert.Equal(10, read.SearchWords.Count);
        Assert.Equal("word10", read.SearchWords[^1]);
    }

    [Theory]
    [InlineData("\"31/10/2026\"")]
    [InlineData("\"2026-02-30\"")]
    [InlineData("12")]
    public void Read_drops_a_date_that_is_not_a_day(string date)
    {
        var read = GeneralDocumentFields.Read(Json($$"""{ "autoTitle": "Letter", "documentDate": {{date}} }"""));

        Assert.Null(read.DocumentDate);
        Assert.Equal("Letter", read.AutoTitle);
    }

    [Fact]
    public void Read_wrong_kinds_and_blanks_are_empty()
    {
        var read = GeneralDocumentFields.Read(Json("""{ "autoTitle": 4, "summary": "  ", "searchWords": "Eskom" }"""));

        Assert.True(read.IsEmpty);
        Assert.True(GeneralDocumentFields.Read(Json("[]")).IsEmpty);
    }

    [Fact]
    public void SearchText_is_lower_case_and_skips_blanks()
    {
        Assert.Equal(
            "fleet\nkeep for sars\neskom statement\nelectricity.\neskom\n4410",
            GeneralDocumentFields.SearchText("Fleet", " Keep for SARS ", "Eskom statement", "Electricity.", ["Eskom", "4410"]));
        Assert.Null(GeneralDocumentFields.SearchText(null, " "));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
