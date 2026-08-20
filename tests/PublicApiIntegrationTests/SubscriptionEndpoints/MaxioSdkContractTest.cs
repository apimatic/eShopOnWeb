using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class MaxioSdkContractTest
{
    [TestMethod]
    public async Task CardlessSubscriptionSendsInvoiceCollectionMethod()
    {
        var handler = new RecordingHandler();
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials
            {
                Username = "not-a-secret",
                Password = "x"
            }
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = "eshop-pro",
                CustomerReference = "eshop-user:test-user",
                Reference = "eshop-sub:test-request",
                PaymentCollectionMethod = CollectionMethod.Invoice
            }
        };

        await client.Subscriptions.CreateSubscription(request, ct: default);

        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        StringAssert.Contains(handler.Requests[0].Body, "\"product_handle\":\"eshop-pro\"");
        StringAssert.Contains(handler.Requests[0].Body, "\"customer_reference\":\"eshop-user:test-user\"");
        StringAssert.Contains(handler.Requests[0].Body, "\"payment_collection_method\":\"invoice\"");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"subscription\":null}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Body);
}
