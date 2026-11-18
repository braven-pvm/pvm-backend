using Pvm.Infrastructure.Reporting.SellOut;

namespace Pvm.Infrastructure.Tests.Reporting.SellOut;

public sealed class GraphMailParserTests
{
    // Made-up values in the shape of Microsoft Graph v1.0 responses.
    [Fact]
    public void A_message_gives_its_sender_and_how_exchange_accepted_it()
    {
        const string json = """
            {
              "value": [
                {
                  "id": "AAMk1", "subject": "Makro report", "receivedDateTime": "2026-11-18T06:30:00Z", "hasAttachments": true,
                  "from": { "emailAddress": { "name": "Person", "address": "person@pvm.co.za" } },
                  "internetMessageHeaders": [
                    { "name": "Received", "value": "from example" },
                    { "name": "X-MS-Exchange-Organization-AuthAs", "value": "Internal" }
                  ]
                },
                { "id": "AAMk2", "receivedDateTime": "2026-11-18T07:00:00Z", "hasAttachments": false }
              ]
            }
            """;

        var mails = GraphMailParser.ParseMessages(json);

        Assert.Equal(2, mails.Count);
        Assert.Equal(("AAMk1", "person@pvm.co.za", "Internal", true), (mails[0].Id, mails[0].SenderAddress, mails[0].AuthenticatedAs, mails[0].HasAttachments));
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 6, 30, 0, TimeSpan.Zero), mails[0].ReceivedAt);
        Assert.Null(mails[1].SenderAddress);
        Assert.Null(mails[1].AuthenticatedAs);
    }

    [Fact]
    public void Only_file_attachments_are_read()
    {
        const string json = """
            {
              "value": [
                { "@odata.type": "#microsoft.graph.fileAttachment", "name": "Makro.xls", "contentType": "application/vnd.ms-excel", "contentBytes": "AQID" },
                { "@odata.type": "#microsoft.graph.itemAttachment", "name": "Forwarded mail" },
                { "@odata.type": "#microsoft.graph.referenceAttachment", "name": "Link to a file" }
              ]
            }
            """;

        var file = Assert.Single(GraphMailParser.ParseAttachments(json));

        Assert.Equal("Makro.xls", file.Name);
        Assert.Equal([1, 2, 3], file.Content);
    }

    [Fact]
    public void A_folder_id_comes_from_a_list_or_from_a_new_folder()
    {
        Assert.Equal("F1", GraphMailParser.FolderId("""{ "value": [ { "id": "F1", "displayName": "Loaded" } ] }"""));
        Assert.Null(GraphMailParser.FolderId("""{ "value": [] }"""));
        Assert.Equal("F2", GraphMailParser.FolderId("""{ "id": "F2", "displayName": "Failed" }"""));
    }
}
