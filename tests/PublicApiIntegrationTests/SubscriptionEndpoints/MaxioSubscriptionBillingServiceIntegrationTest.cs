using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Exercises the Maxio subscription billing service directly against the sandbox.
/// Skipped (inconclusive) when the MAXIO_* environment variables are not present.
/// </summary>
[TestClass]
public class MaxioSubscriptionBillingServiceIntegrationTest
{
    private readonly MaxioOptions _options = new()
    {
        ApiKey = Environment.GetEnvironmentVariable(MaxioOptions.EnvApiKey),
        Subdomain = Environment.GetEnvironmentVariable(MaxioOptions.EnvSubdomain),
        ProductFamilyHandle = Environment.GetEnvironmentVariable(MaxioOptions.EnvProductFamilyHandle)
    };

    [TestInitialize]
    public void EnsureMaxioConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            string.IsNullOrWhiteSpace(_options.Subdomain) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            Assert.Inconclusive("MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / MAXIO_DEFAULT_PRODUCT_FAMILY are not set; skipping subscription billing tests.");
        }
    }

    [TestMethod]
    public async Task ListsPlansReturnsConfiguredFamilyPlans()
    {
        var service = CreateService();
        var plans = await service.ListPlansAsync();

        var pro = plans.FirstOrDefault(p => p.Handle == "eshop-pro");
        Assert.IsNotNull(pro, "Expected the eshop-pro plan in the configured family.");
        Assert.AreEqual(29900, pro!.PriceInCents);
        Assert.IsTrue(plans.Any(p => p.Handle == "basic-plan"));
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentAndListable()
    {
        var customerReference = $"eshop-apitest-{Guid.NewGuid():N}@example.com";
        var service = CreateService();

        long subscriptionId = 0;
        long customerId = 0;
        try
        {
            var first = await service.SubscribeAsync(customerReference, customerReference, "eshop-pro");
            Assert.AreEqual(true, first.WasCreated);
            Assert.AreEqual("active", first.Subscription.State);
            Assert.AreEqual("eshop-pro", first.Subscription.ProductHandle);
            Assert.AreEqual(29900, first.Subscription.ProductPriceInCents);
            Assert.IsNotNull(first.Subscription.Currency);
            Assert.IsNotNull(first.Subscription.CurrentPeriodEndsAt);

            subscriptionId = first.Subscription.SubscriptionId;
            customerId = first.Subscription.CustomerId;

            var second = await service.SubscribeAsync(customerReference, customerReference, "eshop-pro");
            Assert.AreEqual(false, second.WasCreated);
            Assert.AreEqual(subscriptionId, second.Subscription.SubscriptionId);

            var mine = await service.ListSubscriptionsAsync(customerReference);
            Assert.IsTrue(mine.Any(s => s.SubscriptionId == subscriptionId && s.ProductHandle == "eshop-pro"));
        }
        finally
        {
            if (subscriptionId != 0)
            {
                await MaxioTestClient.PurgeSubscriptionAsync(subscriptionId, customerId);
            }
        }
    }

    [TestMethod]
    public async Task ListSubscriptionsIsEmptyForUnknownCustomer()
    {
        var service = CreateService();
        var reference = $"eshop-apitest-nobody-{Guid.NewGuid():N}@example.com";

        var subscriptions = await service.ListSubscriptionsAsync(reference);

        Assert.AreEqual(0, subscriptions.Count);
    }

    private MaxioSubscriptionBillingService CreateService()
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.ResolveApiBaseUrl())
        };
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        return new MaxioSubscriptionBillingService(httpClient, _options, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }
}
