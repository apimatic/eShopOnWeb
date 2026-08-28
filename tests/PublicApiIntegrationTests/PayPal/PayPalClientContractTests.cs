using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.PayPal;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PayPal;

[TestClass]
public class PayPalClientContractTests
{
    [TestMethod]
    public async Task CreateOrderUsesSpecPathBaseOverrideAndIdempotencyHeader()
    {
        var requests = new List<CapturedRequest>();
        var handler = new RecordingHandler(requests, request => request.RequestUri!.AbsolutePath switch
        {
            "/paypal/v1/oauth2/token" => Json("{\"access_token\":\"token\",\"expires_in\":3600}"),
            "/paypal/v2/checkout/orders" => Json("{\"id\":\"ORDER-1\",\"status\":\"CREATED\"}", HttpStatusCode.Created),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var client = CreateClient(handler);

        var response = await client.CreateOrderAsync(42, "eshop-reference", 8.50m, "USD", default);

        Assert.AreEqual("ORDER-1", response.Id);
        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual("https://mock.invalid/paypal/v1/oauth2/token", requests[0].Uri);
        Assert.AreEqual("grant_type=client_credentials", requests[0].Body);
        Assert.AreEqual("https://mock.invalid/paypal/v2/checkout/orders", requests[1].Uri);
        Assert.AreEqual("eshop-reference-create", requests[1].PayPalRequestId);
        StringAssert.Contains(requests[1].Body, "\"intent\":\"AUTHORIZE\"");
        StringAssert.Contains(requests[1].Body, "\"value\":\"8.50\"");
        StringAssert.Contains(requests[1].Body, "\"invoice_id\":\"eshop-reference\"");
    }

    [TestMethod]
    public async Task TransactionSearchReadsEveryReportedPage()
    {
        var requests = new List<CapturedRequest>();
        var handler = new RecordingHandler(requests, request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/v1/oauth2/token"))
                return Json("{\"access_token\":\"token\",\"expires_in\":3600}");
            var query = request.RequestUri.Query;
            return query.Contains("page=1", StringComparison.Ordinal)
                ? Json("{\"transaction_details\":[{\"transaction_info\":{\"transaction_id\":\"TXN-1\"}}],\"page\":1,\"total_pages\":2}")
                : Json("{\"transaction_details\":[{\"transaction_info\":{\"transaction_id\":\"TXN-2\"}}],\"page\":2,\"total_pages\":2}");
        });
        var client = CreateClient(handler);

        var transactions = await client.SearchTransactionsAsync(DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow, default);

        CollectionAssert.AreEquivalent(new[] { "TXN-1", "TXN-2" },
            transactions.Select(x => x.TransactionInfo.TransactionId).ToArray());
        Assert.AreEqual(2, requests.Count(x => x.Uri.Contains("/v1/reporting/transactions", StringComparison.Ordinal)));
    }

    private static PayPalClient CreateClient(HttpMessageHandler handler) => new(
        new SingleClientFactory(new HttpClient(handler)),
        Options.Create(new PayPalSettings
        {
            ClientId = "client",
            ClientSecret = "secret",
            Environment = "sandbox",
            Currency = "USD",
            BaseUrl = "https://mock.invalid/paypal"
        }));

    private static HttpResponseMessage Json(string content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(string Uri, string Body, string? PayPalRequestId);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<CapturedRequest> _requests;
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
        public RecordingHandler(List<CapturedRequest> requests, Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _requests = requests;
            _response = response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            _requests.Add(new CapturedRequest(request.RequestUri!.ToString(), body,
                request.Headers.TryGetValues("PayPal-Request-Id", out var values) ? values.Single() : null));
            return _response(request);
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }
}
