using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CyberSourceMergedSpec;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.Infrastructure.Invoicing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Invoicing;

public class CyberSourceInvoicingServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public string? LastRequestBody { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (CyberSourceInvoicingService Service, StubHandler Handler) BuildService(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        var client = new CyberSourceMergedSpecClient(new HttpClient(handler), new CyberSourceMergedSpecClientOptions());
        var settings = Options.Create(new VisaSettings { BaseUrl = "https://provider.test/", RequestTimeoutSeconds = 30 });
        var logger = Substitute.For<IAppLogger<CyberSourceInvoicingService>>();
        return (new CyberSourceInvoicingService(client, settings, logger), handler);
    }

    private static RaiseInvoiceCommand SampleRaiseCommand() => new()
    {
        Description = "eShopOnWeb order 1",
        TotalAmount = 39.00m,
        Currency = "USD",
        DueDate = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero),
        CustomerName = "Ada Lovelace",
        CustomerEmail = "ada@example.com",
        InvoiceNumber = "ESHOP-1-ABCDEF",
        LineItems = new List<InvoiceLineItemDetail> { new("1", ".NET Bot Black Sweatshirt", 2, 19.50m) }
    };

    [Fact]
    public async Task RaiseInvoice_SendsDraftRequest_AndMapsResponse()
    {
        var (service, handler) = BuildService(_ =>
            Json(HttpStatusCode.Created, """{ "id": "INV-1", "status": "DRAFT" }"""));

        var result = await service.RaiseInvoiceAsync(SampleRaiseCommand());

        Assert.Equal("INV-1", result.ProviderInvoiceId);
        Assert.Equal("DRAFT", result.Status);

        // Draft-on-create: the request must not force immediate delivery.
        Assert.DoesNotContain("\"sendImmediately\":true", handler.LastRequestBody);
        // Amounts are strings, currency is USD, and each line carries a SKU (the provider requires it).
        Assert.Contains("\"totalAmount\":\"39.00\"", handler.LastRequestBody);
        Assert.Contains("\"currency\":\"USD\"", handler.LastRequestBody);
        Assert.Contains("\"productSku\":\"1\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RaiseInvoice_WhenProviderRejects_ThrowsInvoicingProviderExceptionCarrying400()
    {
        var errorBody = """
        {
          "submitTimeUtc": "2026-08-31T10:00:00Z",
          "status": "BAD_REQUEST",
          "reason": "VALIDATION_ERRORS",
          "message": "Field validation errors",
          "details": [ { "field": "orderInformation.lineItems.productSku", "reason": "required" } ]
        }
        """;
        var (service, _) = BuildService(_ => Json(HttpStatusCode.BadRequest, errorBody));

        var ex = await Assert.ThrowsAsync<InvoicingProviderException>(() => service.RaiseInvoiceAsync(SampleRaiseCommand()));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("Field validation errors", ex.Message);
    }

    [Fact]
    public async Task GetInvoice_MapsStatusPaymentLinkAndHistory()
    {
        var getBody = """
        {
          "id": "INV-1",
          "status": "SENT",
          "invoiceInformation": { "paymentLink": "https://pay.test/INV-1" },
          "invoiceHistory": [
            { "event": "CREATE", "date": "2026-08-31T10:00:00Z" },
            { "event": "SEND", "date": "2026-08-31T10:05:00Z" }
          ]
        }
        """;
        var (service, _) = BuildService(_ => Json(HttpStatusCode.OK, getBody));

        var result = await service.GetInvoiceAsync("INV-1");

        Assert.Equal("SENT", result.Status);
        Assert.Equal("https://pay.test/INV-1", result.PaymentLink);
        Assert.Equal(2, result.History.Count);
        Assert.Equal("CREATE", result.History[0].Event);
    }

    [Fact]
    public async Task WithdrawInvoice_MapsResultingStatus()
    {
        var (service, handler) = BuildService(_ => Json(HttpStatusCode.OK, """{ "id": "INV-1", "status": "CANCELED" }"""));

        var result = await service.WithdrawInvoiceAsync("INV-1");

        Assert.Equal("CANCELED", result.Status);
        Assert.Equal(HttpMethod.Post, handler.Requests[^1].Method);
        Assert.Contains("/cancelation", handler.Requests[^1].RequestUri!.AbsolutePath);
    }
}
