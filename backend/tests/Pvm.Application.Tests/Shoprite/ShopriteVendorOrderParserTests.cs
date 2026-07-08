using Pvm.Application.Shoprite;

namespace Pvm.Application.Tests.Shoprite;

public sealed class ShopriteVendorOrderParserTests
{
    [Fact]
    public void Parse_ExtractsPurchaseOrderHeaderDeliveryLocationAndLines()
    {
        var batch = ShopriteVendorOrderParser.Parse(SampleVendorOrderJson);

        var order = Assert.Single(batch.Orders);
        Assert.Equal("1210086350", order.PurchaseOrderNumber);
        Assert.Equal("216532891", order.OrderHeaderId);
        Assert.Equal("220", order.OrderTypeCode);
        Assert.Equal("Normal", order.OrderTypeLabel);
        Assert.Equal("6001197000006", order.SupplierGln);
        Assert.Equal("6001001570701", order.BuyerGln);
        Assert.Equal("6001001570701", order.DeliveryGln);
        Assert.Equal("57073", order.DeliveryLocationCode);
        Assert.Equal("CHECKERS FX YORK ST CENTRE", order.DeliveryLocationName);
        Assert.Equal("buyer", order.DeliveryLocationSource);
        Assert.Equal("ZAR", order.CurrencyCode);
        Assert.Equal(226.26m, order.TotalExcludingTax);
        Assert.Equal(260.20m, order.TotalIncludingTax);

        var line = Assert.Single(order.Lines);
        Assert.Equal(1, line.LineNumber);
        Assert.Equal("06001197181125", line.Gtin);
        Assert.Equal("10369732", line.BuyerItemId);
        Assert.Equal("ENERGY BAR PVM 45G, CMEL", line.BuyerItemDescription);
        Assert.Equal(1m, line.RequestedQuantity);
        Assert.Equal(226.26m, line.NetAmount);
        Assert.Equal(260.20m, line.NetPrice);
    }

    [Fact]
    public void Parse_PrefersShipToDeliveryLocationWhenPresent()
    {
        var json = SampleVendorOrderJson.Replace(
            "\"buyerField\": {",
            "\"shipToField\": { \"glnField\": \"6001009999999\", \"additionalPartyIdentificationField\": [ { \"additionalPartyIdentificationTypeCodeField\": \"BUYER_ASSIGNED_IDENTIFIER_FOR_A_PARTY\", \"valueField\": \"999\" }, { \"additionalPartyIdentificationTypeCodeField\": \"BUYER_ASSIGNED_DESCRIPTION_FOR_A_PARTY\", \"valueField\": \"SHOPRITE DC\" } ] }, \"buyerField\": {");

        var order = Assert.Single(ShopriteVendorOrderParser.Parse(json).Orders);

        Assert.Equal("6001009999999", order.DeliveryGln);
        Assert.Equal("999", order.DeliveryLocationCode);
        Assert.Equal("SHOPRITE DC", order.DeliveryLocationName);
        Assert.Equal("shipTo", order.DeliveryLocationSource);
    }

    private const string SampleVendorOrderJson = """
        {
          "standardBusinessDocumentHeaderField": {},
          "orderField": [
            {
              "orderIdentificationField": {
                "entityIdentificationField": "1210086350",
                "contentOwnerField": {
                  "additionalPartyIdentificationField": [
                    {
                      "additionalPartyIdentificationTypeCodeField": "BUYER_ASSIGNED_ORDER_HEADERID",
                      "valueField": "216532891"
                    }
                  ]
                }
              },
              "orderTypeCodeField": {
                "codeListVersionField": "Normal",
                "valueField": "220"
              },
              "buyerField": {
                "glnField": "6001001570701",
                "additionalPartyIdentificationField": [
                  {
                    "valueField": "57073",
                    "additionalPartyIdentificationTypeCodeField": "BUYER_ASSIGNED_IDENTIFIER_FOR_A_PARTY"
                  },
                  {
                    "valueField": "CHECKERS FX YORK ST CENTRE",
                    "additionalPartyIdentificationTypeCodeField": "BUYER_ASSIGNED_DESCRIPTION_FOR_A_PARTY"
                  }
                ]
              },
              "sellerField": {
                "glnField": "6001197000006"
              },
              "totalMonetaryAmountExcludingTaxesField": {
                "currencyCodeField": "ZAR",
                "valueField": 226.26
              },
              "totalMonetaryAmountIncludingTaxesField": {
                "currencyCodeField": "ZAR",
                "valueField": 260.20
              },
              "creationDateTimeField": "2026-05-20T11:25:54.5362865+02:00",
              "orderLineItemField": [
                {
                  "lineItemNumberField": 1,
                  "requestedQuantityField": {
                    "valueField": 1.0
                  },
                  "netAmountField": {
                    "currencyCodeField": "ZAR",
                    "valueField": 226.26
                  },
                  "netPriceField": {
                    "currencyCodeField": "ZAR",
                    "valueField": 260.20
                  },
                  "transactionalTradeItemField": {
                    "gtinField": "06001197181125",
                    "tradeItemDescriptionField": {
                      "valueField": "ENERGY BAR PVM 45G, CMEL"
                    },
                    "additionalTradeItemIdentificationField": [
                      {
                        "additionalTradeItemIdentificationTypeCodeField": "BUYER_ASSIGNED_ITEMID",
                        "valueField": "10369732"
                      },
                      {
                        "additionalTradeItemIdentificationTypeCodeField": "BUYER_ASSIGNED_ITEM_DESCRIPTION",
                        "valueField": "ENERGY BAR PVM 45G, CMEL"
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;
}

