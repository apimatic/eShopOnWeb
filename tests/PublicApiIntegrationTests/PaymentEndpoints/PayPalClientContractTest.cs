using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PayPalClientContractTest
{
    [TestMethod]
    public async Task SearchTraversesEveryPageAndEveryThirtyOneDayChunk()
    {
        var handler = new ReportingHandler();
        var options = Options.Create(new PayPalOptions
        {
            ClientId = "client",
            ClientSecret = "secret",
            Environment = "sandbox",
            Currency = "USD",
            BaseUrl = "https://contract.test/paypal-root"
        });
        var client = new PayPalClient(new HttpClient(handler), options);

        var transactions = await client.SearchAllTransactionsAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.AreEqual(3, transactions.Count);
        Assert.AreEqual(3, handler.ReportingUris.Count);
        Assert.IsTrue(handler.ReportingUris.All(x =>
            x.AbsolutePath == "/paypal-root/v1/reporting/transactions"));
        Assert.IsTrue(handler.ReportingUris[0].Query.Contains("page=1"));
        Assert.IsTrue(handler.ReportingUris[1].Query.Contains("page=2"));
        Assert.IsTrue(handler.ReportingUris[2].Query.Contains("page=1"));
        Assert.AreEqual("/paypal-root/v1/oauth2/token", handler.TokenUri!.AbsolutePath);
    }

    private sealed class ReportingHandler : HttpMessageHandler
    {
        public Uri? TokenUri { get; private set; }
        public List<Uri> ReportingUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/v1/oauth2/token", StringComparison.Ordinal))
            {
                TokenUri = request.RequestUri;
                return Json(HttpStatusCode.OK, """
                    {"access_token":"access","expires_in":3600}
                    """);
            }

            Assert.IsFalse(request.Headers.Contains("Prefer"),
                "Transaction Search does not define the Prefer header.");
            ReportingUris.Add(request.RequestUri);
            var call = ReportingUris.Count;
            var totalPages = call == 1 ? 2 : 1;
            var id = call.ToString("00000000000000000");
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                transaction_details = new[]
                {
                    new
                    {
                        transaction_info = new
                        {
                            transaction_id = id,
                            transaction_initiation_date = "2026-01-01T00:00:00Z",
                            transaction_updated_date = "2026-01-01T00:00:00Z",
                            transaction_amount = new { currency_code = "USD", value = "1.00" },
                            transaction_status = "S"
                        }
                    }
                },
                page = 1,
                total_items = totalPages,
                total_pages = totalPages
            }));
        }

        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
