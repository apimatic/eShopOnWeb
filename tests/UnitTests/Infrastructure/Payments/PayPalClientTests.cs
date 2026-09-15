using System.Net;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

public class PayPalClientTests
{
    [Fact]
    public async Task TransactionSearchChunksLongRangesAndReadsEveryPageFromTheConfiguredBaseUrl()
    {
        var handler = new RecordingHandler();
        var options = Options.Create(new PayPalOptions
        {
            ClientId = "client",
            ClientSecret = "secret",
            Environment = "Sandbox",
            Currency = "USD",
            BaseUrl = "https://override.test/paypal-root"
        });
        var client = new PayPalClient(new HttpClient(handler), options);

        var result = await client.SearchTransactionsAsync(
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-02T00:00:00Z"), CancellationToken.None);

        Assert.Empty(result.Transactions);
        Assert.Single(handler.Requests.Where(x => x.EndsWith("/paypal-root/v1/oauth2/token")));
        var reportingRequests = handler.Requests.Where(x => x.Contains("/paypal-root/v1/reporting/transactions"))
            .ToList();
        Assert.Equal(4, reportingRequests.Count);
        Assert.Equal(2, reportingRequests.Count(x => x.Contains("page=1")));
        Assert.Equal(2, reportingRequests.Count(x => x.Contains("page=2")));
        Assert.All(handler.Requests, x => Assert.StartsWith("https://override.test/paypal-root/", x));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            var isToken = request.RequestUri.AbsolutePath.EndsWith("/v1/oauth2/token");
            var json = isToken
                ? "{\"access_token\":\"token\",\"expires_in\":3600}"
                : "{\"transaction_details\":[],\"total_pages\":2,\"last_refreshed_datetime\":\"2026-02-02T00:00:00Z\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
