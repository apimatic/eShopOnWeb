using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CyberSourceMergedSpec;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;
using Microsoft.eShopWeb.Infrastructure.Invoicing;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.InvoiceEndpoints;

/// <summary>
/// Exercises the SDK-facing gateway through the SDK's own HttpClient seam (a stub handler), so no real
/// network call happens. Verifies the wire request eShop sends and how provider responses/errors map back.
/// </summary>
[TestClass]
public class CyberSourceInvoiceGatewayTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static CyberSourceInvoiceGateway CreateGateway(StubHandler handler)
    {
        var client = new CyberSourceMergedSpecClient(new HttpClient(handler), new CyberSourceMergedSpecClientOptions());
        var settings = Options.Create(new VisaSettings
        {
            BaseUrl = "https://apitest.cybersource.com/",
            MerchantId = "m",
            KeyId = "k",
            SecretKey = "cw==",
            RequestTimeoutSeconds = 10,
        });
        return new CyberSourceInvoiceGateway(client, settings);
    }

    private static NewInvoiceRequest SampleRequest() => new(
        Description: "eShopOnWeb order #7",
        DueDate: new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
        TotalAmount: "20.00",
        Currency: "USD",
        CustomerName: "buyer",
        CustomerEmail: "buyer@example.com",
        MerchantCustomerId: "eshop-7",
        InvoiceNumber: "ESHOP-7-abc123",
        Lines: new List<InvoiceLine> { new("SKU-5", "Widget", "10.00", 2) });

    [TestMethod]
    public async Task RaiseAsync_SendsDraftUsdInvoice_AndReturnsProviderId()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """{"id":"PROV-STUB","status":"DRAFT"}"""));
        var gateway = CreateGateway(handler);

        var receipt = await gateway.RaiseAsync(SampleRequest(), CancellationToken.None);

        Assert.AreEqual("PROV-STUB", receipt.ProviderInvoiceId);
        Assert.AreEqual("DRAFT", receipt.Status);

        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        var sent = handler.Bodies[0];
        StringAssert.Contains(sent, "\"USD\"");
        StringAssert.Contains(sent, "\"totalAmount\":\"20.00\"");
        StringAssert.Contains(sent, "\"sendImmediately\":false");   // raised as a draft, not delivered
        StringAssert.Contains(sent, "ESHOP-7-abc123");
        StringAssert.Contains(sent, "eshop-7");
        StringAssert.Contains(sent, "\"productSku\":\"SKU-5\"");   // provider requires a SKU per line
    }

    [TestMethod]
    public async Task GetAsync_MapsStatusAndPaymentLink()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"id":"PROV-STUB","status":"SENT","invoiceInformation":{"paymentLink":"https://pay.example/PROV-STUB"}}"""));
        var gateway = CreateGateway(handler);

        var state = await gateway.GetAsync("PROV-STUB", CancellationToken.None);

        Assert.AreEqual("PROV-STUB", state.ProviderInvoiceId);
        Assert.AreEqual("SENT", state.Status);
        Assert.AreEqual("https://pay.example/PROV-STUB", state.PaymentLink);
    }

    [TestMethod]
    public async Task ProviderError_IsTranslatedToInvoiceProviderException_WithStatus()
    {
        // 404 is not a retryable status, so the stub is hit once.
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        var gateway = CreateGateway(handler);

        var ex = await Assert.ThrowsExceptionAsync<InvoiceProviderException>(() =>
            gateway.GetAsync("missing", CancellationToken.None));

        Assert.AreEqual(404, ex.StatusCode);
    }

    [TestMethod]
    public async Task TransportFailure_IsTranslatedToInvoiceProviderException_WithoutStatus()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var gateway = CreateGateway(handler);

        var ex = await Assert.ThrowsExceptionAsync<InvoiceProviderException>(() =>
            gateway.GetAsync("any", CancellationToken.None));

        Assert.IsNull(ex.StatusCode);
    }
}
