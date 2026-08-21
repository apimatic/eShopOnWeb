using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public sealed class MaxioSubscriptionBillingServiceTests
{
    private static readonly BillingIdentity Identity =
        new("stable-user-id", "Test", "User", "test@example.com");

    [TestMethod]
    public async Task RepeatedSubscribeSendsOneSubscriptionWrite()
    {
        var stub = new MaxioStubHandler(failSubscriptionWrite: false);
        var service = CreateService(stub);

        var first = await service.SubscribeAsync(Identity, 42, default);
        var second = await service.SubscribeAsync(Identity, 42, default);

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(1, stub.CustomerPostCount);
        Assert.AreEqual(1, stub.SubscriptionPostCount);
        Assert.IsFalse(stub.SubscriptionBody.Contains("payment_profile", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(stub.SubscriptionBody.Contains("credit_card", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(stub.SubscriptionBody.Contains("bank_account", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task TransportFailureDoesNotResendSubscriptionWrite()
    {
        var stub = new MaxioStubHandler(failSubscriptionWrite: true);
        var service = CreateService(stub);

        var exception = await Assert.ThrowsExceptionAsync<BillingException>(
            () => service.SubscribeAsync(Identity, 42, default));

        Assert.AreEqual(BillingFailureKind.UnknownOutcome, exception.Kind);
        Assert.AreEqual(1, stub.SubscriptionPostCount);
    }

    private static MaxioSubscriptionBillingService CreateService(MaxioStubHandler stub)
    {
        var callContext = new MaxioCallContext();
        var transport = new MaxioTransportHandler(callContext) { InnerHandler = stub };
        var httpClient = new HttpClient(transport) { Timeout = TimeSpan.FromSeconds(5) };

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = "test", Password = "x" },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Delay = TimeSpan.Zero,
                MaxJitter = TimeSpan.Zero,
                Timeout = TimeSpan.FromSeconds(5)
            }
        };
        clientOptions.Server.Production.Us.BaseUrl = "https://maxio.test";

        var dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CatalogContext(dbOptions);

        return new MaxioSubscriptionBillingService(
            new MaxioAdvancedBillingClient(httpClient, clientOptions),
            Options.Create(new MaxioOptions
            {
                ApiKey = "test",
                Subdomain = "test",
                ProductFamilyHandle = "test-family"
            }),
            callContext,
            db,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    private sealed class MaxioStubHandler(bool failSubscriptionWrite) : HttpMessageHandler
    {
        public int CustomerPostCount { get; private set; }
        public int SubscriptionPostCount { get; private set; }
        public string SubscriptionBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Get && path == "/product_families.json")
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product_family\":{\"id\":3023074,\"handle\":\"test-family\",\"name\":\"Subscriptions\"}}]");
            }

            if (request.Method == HttpMethod.Get && path == "/product_families/3023074/products.json")
            {
                return Json(HttpStatusCode.OK,
                    "[{\"product\":{\"id\":42,\"handle\":\"pro\",\"name\":\"Pro\",\"description\":\"Pro plan\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\",\"default_product_price_point_id\":99,\"product_price_point_id\":99,\"product_price_point_handle\":\"default\",\"product_price_point_name\":\"Default\"}}]");
            }

            if (request.Method == HttpMethod.Get && path == "/subscriptions/lookup.json")
            {
                return Json(HttpStatusCode.NotFound, string.Empty);
            }

            if (request.Method == HttpMethod.Get && path == "/customers/lookup.json")
            {
                return Json(HttpStatusCode.NotFound, string.Empty);
            }

            if (request.Method == HttpMethod.Post && path == "/customers.json")
            {
                CustomerPostCount++;
                return Json(HttpStatusCode.Created,
                    "{\"customer\":{\"id\":101,\"reference\":\"customer-reference\",\"first_name\":\"Test\",\"last_name\":\"User\",\"email\":\"test@example.com\"}}");
            }

            if (request.Method == HttpMethod.Post && path == "/subscriptions.json")
            {
                SubscriptionPostCount++;
                SubscriptionBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (failSubscriptionWrite)
                {
                    throw new HttpRequestException("simulated connection reset");
                }

                return Json(HttpStatusCode.Created,
                    "{\"subscription\":{\"id\":501,\"reference\":\"subscription-reference\",\"state\":\"active\",\"product_price_in_cents\":29900,\"next_assessment_at\":\"2030-01-01T00:00:00Z\",\"product\":{\"id\":42,\"handle\":\"pro\",\"name\":\"Pro\"}}}");
            }

            return Json(HttpStatusCode.NotFound, string.Empty);
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
            new(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }
}
