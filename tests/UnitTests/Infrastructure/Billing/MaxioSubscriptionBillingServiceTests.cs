using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioSubscriptionBillingServiceTests
{
    private static readonly BillingBuyer Buyer = new("buyer-1", "demouser@microsoft.com", "demouser", "Customer");

    [Fact]
    public async Task ListPlansAsync_ReturnsMappedPlansFromProductFamily()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            [
              {
                "product": {
                  "id": 1,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "description": "Pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month"
                }
              }
            ]
            """));
        var service = CreateService(handler);

        var plans = await service.ListPlansAsync(CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal("Pro Plan", plans[0].Name);
        Assert.Equal(299.00m, plans[0].Price);
        Assert.Contains("product_families", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("eshop-subscribe", Uri.UnescapeDataString(handler.LastRequest.RequestUri.AbsolutePath));
    }

    [Fact]
    public async Task SubscribeAsync_CreatesCustomerAndSubscriptionWithoutPaymentFields()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("customers") && path.Contains("lookup"))
            {
                return Json(HttpStatusCode.NotFound, """{"errors":["Not Found"]}""");
            }

            if (path.Contains("subscriptions") && path.Contains("lookup"))
            {
                return Json(HttpStatusCode.NotFound, "{}");
            }

            if (request.Method == HttpMethod.Post && path.Contains("customers"))
            {
                return Json(HttpStatusCode.OK, """
                    { "customer": { "id": 42, "reference": "buyer-1", "email": "demouser@microsoft.com", "first_name": "demouser", "last_name": "Customer" } }
                    """);
            }

            if (request.Method == HttpMethod.Post && path.Contains("subscriptions"))
            {
                return Json(HttpStatusCode.OK, """
                    {
                      "subscription": {
                        "id": 99,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "next_assessment_at": "2026-09-19T00:00:00Z",
                        "product": { "handle": "eshop-pro", "name": "Pro Plan", "price_in_cents": 29900 }
                      }
                    }
                    """);
            }

            return Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None);

        Assert.Equal(99, result.Id);
        Assert.Equal("eshop-pro", result.ProductHandle);
        Assert.Equal("active", result.State);
        Assert.Equal(299.00m, result.Price);

        var create = handler.Bodies.Last(b => b.Method == HttpMethod.Post && b.Path.Contains("subscriptions"));
        var compact = create.Body.Replace(" ", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", compact);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", compact);
        Assert.DoesNotContain("chargify_token", compact);
        Assert.DoesNotContain("payment_profile", compact);
        Assert.DoesNotContain("credit_card", compact);
    }

    [Fact]
    public async Task SubscribeAsync_ReturnsExistingSubscriptionOnDoubleClick()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("customers") && path.Contains("lookup"))
            {
                return Json(HttpStatusCode.OK, """{ "customer": { "id": 42, "reference": "buyer-1" } }""");
            }

            if (path.Contains("subscriptions") && path.Contains("lookup"))
            {
                return Json(HttpStatusCode.OK, """
                    {
                      "subscription": {
                        "id": 99,
                        "state": "active",
                        "product_price_in_cents": 29900,
                        "product": { "handle": "eshop-pro", "name": "Pro Plan" }
                      }
                    }
                    """);
            }

            return Json(HttpStatusCode.InternalServerError, "{}");
        });
        var service = CreateService(handler);

        var result = await service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None);

        Assert.Equal(99, result.Id);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ListPlansAsync_MapsProviderNotFoundToBillingException()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound, "\"missing family\""));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<BillingException>(() => service.ListPlansAsync(CancellationToken.None));

        Assert.Equal((int)HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("Subscription plans are not available.", ex.Message);
    }

    private static MaxioSubscriptionBillingService CreateService(StubHandler handler)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var configuration = Substitute.For<IConfiguration>();
        configuration["Maxio:ProductFamilyHandle"].Returns("eshop-subscribe");
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        return new MaxioSubscriptionBillingService(client, configuration, logger);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<(HttpMethod Method, string Path, string Body)> Bodies { get; } = new();
        public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Bodies.Add((request.Method, request.RequestUri?.AbsolutePath ?? string.Empty, body));
            return _responder(request);
        }
    }
}
