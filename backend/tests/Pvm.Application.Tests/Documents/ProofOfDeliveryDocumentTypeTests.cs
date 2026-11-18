using System.Text.Json;
using Pvm.Application.Documents;
using Xunit;

namespace Pvm.Application.Tests.Documents;

public class ProofOfDeliveryDocumentTypeTests
{
    [Fact]
    public void Field_schema_is_a_closed_object_with_every_property_required()
    {
        var schema = new ProofOfDeliveryDocumentType().FieldSchema;

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());

        var properties = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToList();
        var required = schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()!).Order().ToList();
        Assert.Equal(properties, required);
        Assert.Equal(
            typeof(PodExtraction).GetProperties().Select(p => p.Name).Order().ToList(),
            properties);
    }
}
