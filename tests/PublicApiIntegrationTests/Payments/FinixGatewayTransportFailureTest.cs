using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class FinixGatewayTransportFailureTest
{
    [TestMethod]
    public async Task AuthorizeReportsAnUnknownOutcomeWhenTheTransportFails()
    {
        var exception = await Assert.ThrowsExceptionAsync<FinixProviderException>(
            () => NewGateway().AuthorizeAsync(1950, "PI-1", "eshop-auth-1-key", "ESHOP", CancellationToken.None));

        Assert.IsTrue(exception.OutcomeUnknown);
    }

    [TestMethod]
    public async Task CaptureReportsAnUnknownOutcomeWhenTheTransportFails()
    {
        var exception = await Assert.ThrowsExceptionAsync<FinixProviderException>(
            () => NewGateway().CaptureAsync("AUTH-1", 1950, CancellationToken.None));

        Assert.IsTrue(exception.OutcomeUnknown);
    }

    [TestMethod]
    public async Task VoidReportsAnUnknownOutcomeWhenTheTransportFails()
    {
        var exception = await Assert.ThrowsExceptionAsync<FinixProviderException>(
            () => NewGateway().VoidAsync("AUTH-1", CancellationToken.None));

        Assert.IsTrue(exception.OutcomeUnknown);
    }

    [TestMethod]
    public async Task RefundReportsAnUnknownOutcomeWhenTheTransportFails()
    {
        var exception = await Assert.ThrowsExceptionAsync<FinixProviderException>(
            () => NewGateway().RefundAsync("TRANSFER-1", 500, CancellationToken.None));

        Assert.IsTrue(exception.OutcomeUnknown);
    }

    [TestMethod]
    public async Task CreateCardReportsAnUnknownOutcomeWhenTheTransportFails()
    {
        var card = new CardData("5200828282828210", 12, 2029, "022", "Test Shopper", null, null, null, null, null);
        var exception = await Assert.ThrowsExceptionAsync<FinixProviderException>(
            () => NewGateway().CreateCardInstrumentAsync("ID-1", card, "request-1", CancellationToken.None));

        Assert.IsTrue(exception.OutcomeUnknown);
    }

    [TestMethod]
    public async Task CreateIdentityReportsAnUnknownOutcomeWhenTheTransportFails()
    {
        var exception = await Assert.ThrowsExceptionAsync<FinixProviderException>(
            () => NewGateway().CreateBuyerIdentityAsync("shopper@example.com", null, CancellationToken.None));

        Assert.IsTrue(exception.OutcomeUnknown);
    }

    [TestMethod]
    public async Task DisableInstrumentReportsAnUnknownOutcomeWhenTheTransportFails()
    {
        var exception = await Assert.ThrowsExceptionAsync<FinixProviderException>(
            () => NewGateway().DisableInstrumentAsync("PI-1", CancellationToken.None));

        Assert.IsTrue(exception.OutcomeUnknown);
    }

    private static FinixGateway NewGateway()
    {
        var httpClient = new HttpClient(new ThrowingHandler());
        var client = new FinixApiReferenceClient(httpClient, new FinixApiReferenceClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test", Password = "test" },
            Retry = RetryOptions.Disabled(),
            Logging = new LoggingOptions { LoggerFactory = NullLoggerFactory.Instance }
        });

        return new FinixGateway(client, new FinixSettings
        {
            Username = "test",
            Password = "test",
            MerchantId = "test-merchant",
            Environment = "sandbox",
            Currency = "USD"
        }, NullLogger<FinixGateway>.Instance);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("connection reset");
        }
    }
}
