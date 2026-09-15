using System.Net;
using System.Text;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public sealed class PayPalClientReportingTest
{
    [TestMethod]
    public async Task UsesBaseUrlForOAuthAndReadsEveryWindowAndPage()
    {
        var handler = new ReportingHandler();
        var httpClient = new HttpClient(handler);
        var client = new PayPalClient(new SingleClientFactory(httpClient), Options.Create(new PayPalOptions
        {
            ClientId = "client",
            ClientSecret = "secret",
            Environment = "Sandbox",
            Currency = "USD",
            BaseUrl = "https://override.test/paypal"
        }));

        var to = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var transactions = await client.ListTransactionsAsync(to.AddDays(-40), to, CancellationToken.None);

        Assert.AreEqual(4, transactions.Count, "Two pages must be read for each of two <=31-day windows.");
        Assert.AreEqual(4, handler.ReportingCalls);
        Assert.IsTrue(handler.RequestUris.All(uri => uri.StartsWith("https://override.test/paypal/", StringComparison.Ordinal)));
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class ReportingHandler : HttpMessageHandler
    {
        public int ReportingCalls { get; private set; }
        public List<string> RequestUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.AbsolutePath.EndsWith("/v1/oauth2/token", StringComparison.Ordinal))
                return Json("{\"access_token\":\"token\",\"expires_in\":3600}");

            ReportingCalls++;
            var id = $"TXN-{ReportingCalls}";
            var body = $$"""
                {
                  "transaction_details": [{
                    "transaction_info": {
                      "transaction_id": "{{id}}",
                      "transaction_event_code": "T0006",
                      "transaction_status": "S",
                      "transaction_initiation_date": "2026-07-01T00:00:00Z",
                      "transaction_amount": { "currency_code": "USD", "value": "10.00" },
                      "fee_amount": { "currency_code": "USD", "value": "0.50" }
                    }
                  }],
                  "total_pages": 2
                }
                """;
            return Json(body);
        }

        private static Task<HttpResponseMessage> Json(string body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}
