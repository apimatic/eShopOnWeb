using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSdkWireTests
{
    [TestMethod]
    public async Task CreateSubscriptionSendsRemittanceCollectionWithoutPaymentProfile()
    {
        var terminal = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var gateway = CreateGateway(terminal);

        var exception = await Assert.ThrowsExceptionAsync<MaxioIntegrationException>(() =>
            gateway.CreateSubscriptionAsync("plan-handle", "customer-ref", "subscription-ref", default));

        Assert.AreEqual(MaxioFailureKind.InvalidResponse, exception.Kind);
        Assert.AreEqual(1, terminal.SendCount);
        Assert.AreEqual(HttpMethod.Post, terminal.LastMethod);
        StringAssert.Contains(terminal.LastBody!, "\"product_handle\":\"plan-handle\"");
        StringAssert.Contains(terminal.LastBody!, "\"customer_reference\":\"customer-ref\"");
        StringAssert.Contains(terminal.LastBody!, "\"reference\":\"subscription-ref\"");
        StringAssert.Contains(terminal.LastBody!, "\"payment_collection_method\":\"remittance\"");
        Assert.IsFalse(terminal.LastBody.Contains("payment_profile", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(terminal.LastBody.Contains("credit_card", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(terminal.LastBody.Contains("bank_account", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(terminal.LastBody.Contains("product_id", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task TransportFailureCannotResendSubscriptionWrite()
    {
        var terminal = new RecordingHandler(_ => throw new HttpRequestException("connection reset"));
        var gateway = CreateGateway(terminal);

        var exception = await Assert.ThrowsExceptionAsync<MaxioIntegrationException>(() =>
            gateway.CreateSubscriptionAsync("plan-handle", "customer-ref", "subscription-ref", default));

        Assert.AreEqual(MaxioFailureKind.AmbiguousWrite, exception.Kind);
        Assert.AreEqual(1, terminal.SendCount);
    }

    private static MaxioBillingGateway CreateGateway(HttpMessageHandler terminal)
    {
        var guard = new MaxioWriteOnceHandler { InnerHandler = terminal };
        var httpClient = new HttpClient(guard) { Timeout = TimeSpan.FromSeconds(2) };
        var sdkOptions = new MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test-only", Password = "x" },
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(2) }
        };
        sdkOptions.Server.Production.Us.BaseUrl = "https://maxio.invalid";
        var sdkClient = new MaxioAdvancedBilling.MaxioAdvancedBillingClient(httpClient, sdkOptions);
        var settings = Options.Create(new MaxioOptions
        {
            ApiKey = "test-only",
            Subdomain = "test-site",
            ProductFamilyHandle = "test-family",
            BaseUrl = "https://maxio.invalid"
        });
        return new MaxioBillingGateway(
            sdkClient,
            settings,
            NullLogger<MaxioBillingGateway>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private int _sendCount;

        internal RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        internal int SendCount => Volatile.Read(ref _sendCount);
        internal HttpMethod? LastMethod { get; private set; }
        internal string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sendCount);
            LastMethod = request.Method;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }
}
