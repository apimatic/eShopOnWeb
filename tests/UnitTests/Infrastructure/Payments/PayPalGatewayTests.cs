using System.Net;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Payments;

public class PayPalGatewayTests
{
    [Fact]
    public async Task SearchTransactionsReadsEveryPage()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/oauth2/token")
                return Json("{\"access_token\":\"token\",\"expires_in\":3600}");
            var page = request.RequestUri.Query.Contains("page=2", StringComparison.Ordinal) ? 2 : 1;
            return Json("{\"transaction_details\":[{\"transaction_info\":{\"transaction_id\":\"transaction-" +
                page + "\",\"transaction_status\":\"S\",\"transaction_amount\":{\"currency_code\":\"USD\",\"value\":\"1.00\"}}}],\"total_pages\":2}");
        });
        var gateway = Gateway(handler);

        var results = await gateway.SearchTransactionsAsync(DateTimeOffset.UtcNow.AddDays(-2),
            DateTimeOffset.UtcNow.AddDays(-1), CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(new[] { "transaction-1", "transaction-2" }, results.Select(x => x.TransactionId));
        Assert.Contains(handler.Requests, x => x.Query.Contains("page_size=500") && x.Query.Contains("page=2"));
    }

    [Fact]
    public async Task AuthorizeUsesSingleStepAuthorizationReturnedByCreateOrder()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/oauth2/token" => Json("{\"access_token\":\"token\",\"expires_in\":3600}"),
            "/v2/checkout/orders" => Json("""
                {"id":"ORDER","status":"COMPLETED","purchase_units":[{"payments":{"authorizations":[{"id":"AUTH","status":"CREATED","amount":{"currency_code":"USD","value":"12.34"},"create_time":"2026-08-30T00:00:00Z","expiration_time":"2026-09-28T00:00:00Z"}]}}]}
                """, HttpStatusCode.Created),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected request {request.RequestUri}")
        });
        var gateway = Gateway(handler);

        var result = await gateway.AuthorizeAsync("reference", 12.34m, "USD",
            new PayPalPaymentSource("vault-token", null, "VISA ending 1111"), CancellationToken.None);

        Assert.Equal("AUTH", result.AuthorizationId);
        Assert.Equal(12.34m, result.Amount);
        Assert.DoesNotContain(handler.Requests, x => x.AbsolutePath.EndsWith("/authorize", StringComparison.Ordinal));
    }

    private static PayPalGateway Gateway(HttpMessageHandler handler) => new(new HttpClient(handler),
        Options.Create(new PayPalOptions
        {
            ClientId = "client",
            ClientSecret = "secret",
            Environment = "Sandbox",
            Currency = "USD",
            BaseUrl = "https://paypal.test"
        }));

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(response(request));
        }
    }
}
