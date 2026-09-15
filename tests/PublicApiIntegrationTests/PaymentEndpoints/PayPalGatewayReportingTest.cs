using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PayPalGatewayReportingTest
{
    [TestMethod]
    public async Task ReportingUsesBaseUrlChunksLongRangesAndReadsEveryPage()
    {
        var handler = new ReportingHandler();
        var options = Options.Create(new PayPalOptions
        {
            ClientId = "test-client",
            ClientSecret = "test-secret",
            Environment = "Sandbox",
            Currency = "USD",
            BaseUrl = "https://paypal.test/proxy"
        });
        var gateway = new PayPalGateway(new HttpClient(handler), options);

        var transactions = await gateway.ListTransactionsAsync(
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-02-10T00:00:00Z"), CancellationToken.None);

        Assert.AreEqual(4, handler.ReportCalls, "A 40-day range has two windows and every two-page window must be read.");
        Assert.AreEqual(4, transactions.Count);
        Assert.IsTrue(handler.RequestUris.All(x => x.StartsWith("https://paypal.test/proxy/", StringComparison.Ordinal)),
            "The BaseUrl override must be used for token and reporting requests.");
    }

    private sealed class ReportingHandler : HttpMessageHandler
    {
        public int ReportCalls { get; private set; }
        public List<string> RequestUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!.ToString());
            if (request.RequestUri.AbsolutePath.EndsWith("/v1/oauth2/token", StringComparison.Ordinal))
                return Json(new { access_token = "token", expires_in = 3600 });

            ReportCalls++;
            var id = $"TXN{ReportCalls:00000000000000}";
            return Json(new
            {
                transaction_details = new[]
                {
                    new
                    {
                        transaction_info = new
                        {
                            transaction_id = id,
                            transaction_event_code = "T0005",
                            transaction_status = "S",
                            transaction_initiation_date = "2026-01-02T00:00:00Z",
                            transaction_amount = new { currency_code = "USD", value = "1.00" }
                        }
                    }
                },
                total_pages = 2
            });
        }

        private static Task<HttpResponseMessage> Json(object value)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
            });
    }
}
