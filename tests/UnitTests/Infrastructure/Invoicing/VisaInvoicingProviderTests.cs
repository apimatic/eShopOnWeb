using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CyberSourceMergedSpec;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Invoicing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Invoicing;

public class VisaInvoicingProviderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static VisaInvoicingProvider CreateProvider(HttpMessageHandler handler, bool withSendGuard = false)
    {
        HttpMessageHandler pipeline = handler;
        if (withSendGuard)
        {
            pipeline = new SingleSendGuardHandler { InnerHandler = handler };
        }

        var client = new CyberSourceMergedSpecClient(new HttpClient(pipeline), new CyberSourceMergedSpecClientOptions());
        var settings = Options.Create(new VisaSettings { RequestTimeoutSeconds = 30 });
        return new VisaInvoicingProvider(client, settings, NullLogger<VisaInvoicingProvider>.Instance);
    }

    private static RaiseInvoiceRequest SampleRaise() => new(
        Description: "order #1",
        Amount: 20m,
        Currency: "USD",
        DueDate: DateTimeOffset.UtcNow.AddDays(30),
        CustomerName: "Ada",
        CustomerEmail: "ada@example.com",
        MerchantCustomerId: "eshop:ada@example.com",
        LineItems: new List<InvoiceLineItem> { new("Widget", "1", 2, 10m, 20m) });

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Raise_MapsCreatedInvoiceIdAndStatus()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, """{ "id": "INV-9", "status": "DRAFT" }"""));
        var provider = CreateProvider(handler);

        var result = await provider.RaiseAsync(SampleRaise());

        Assert.Equal("INV-9", result.ProviderInvoiceId);
        Assert.Equal("DRAFT", result.Status);
        Assert.Single(handler.Requests.Where(r => r.Method == HttpMethod.Post)); // exactly one send
    }

    [Fact]
    public async Task Raise_OnProvider400_ThrowsProviderExceptionCarrying400AndReason()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest,
            """{ "status": "INVALID_REQUEST", "reason": "INVALID_DATA", "message": "Due date is in the past." }"""));
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<InvoicingProviderException>(() => provider.RaiseAsync(SampleRaise()));

        Assert.Equal(400, ex.ProviderStatusCode);
        Assert.Contains("Due date is in the past.", ex.Message);
    }

    [Fact]
    public async Task Get_OnProvider404_ThrowsProviderExceptionCarrying404()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound,
            """{ "status": "NOT_FOUND", "reason": "NOT_FOUND", "message": "No such invoice." }"""));
        var provider = CreateProvider(handler);

        var ex = await Assert.ThrowsAsync<InvoicingProviderException>(() => provider.GetAsync("missing"));

        Assert.Equal(404, ex.ProviderStatusCode);
    }

    [Fact]
    public async Task Raise_OnTransportFailure_IsNotResent_AndReportsUnknownOutcome()
    {
        // The stub throws instead of answering — a transport failure, which the SDK's pipeline retries on
        // every verb. The single-send guard must stop the retry from reaching the network a second time.
        var handler = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var provider = CreateProvider(handler, withSendGuard: true);

        var ex = await Assert.ThrowsAsync<InvoicingProviderException>(() => provider.RaiseAsync(SampleRaise()));

        Assert.True(ex.OutcomeUnknown);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post)); // no duplicate send
    }
}
