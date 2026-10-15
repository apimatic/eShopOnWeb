#nullable enable
using System;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// Wires the real Maxio SDK client over a <see cref="MaxioStub"/> and a per-test in-memory claim store.
/// Each <see cref="NewService"/> call gets its own DbContext, like a separate HTTP request.
/// </summary>
public sealed class MaxioTestHarness
{
    public const string FamilyHandle = "test-family";
    public const string UserName = "demouser@microsoft.com";
    public const string ProductsPath = "/product_families/handle:" + FamilyHandle + "/products.json";
    public const string CustomerLookupPath = "/customers/lookup.json";
    public const string CustomersPath = "/customers.json";
    public const string SubscriptionsPath = "/subscriptions.json";
    public const string SubscriptionLookupPath = "/subscriptions/lookup.json";
    public static string CustomerSubscriptionsPath(int customerId) => $"/customers/{customerId}/subscriptions.json";

    private readonly DbContextOptions<CatalogContext> _dbOptions;

    public MaxioTestHarness(bool productionRetries = false)
    {
        Stub = new MaxioStub();
        Settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle,
            BaseUrl = "https://maxio.test"
        };
        var options = MaxioBillingServiceCollectionExtensions.BuildClientOptions(Settings, NullLoggerFactory.Instance, TimeProvider.System);
        if (!productionRetries)
        {
            // Keeps failure tests fast; POST is never retried either way.
            options.Retry = RetryOptions.Disabled();
        }
        Client = new MaxioAdvancedBillingClient(new HttpClient(Stub), options);
        _dbOptions = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase("Billing-" + Guid.NewGuid())
            .Options;
    }

    public MaxioStub Stub { get; }

    public MaxioSettings Settings { get; }

    public MaxioAdvancedBillingClient Client { get; }

    public CatalogContext NewDbContext() => new(_dbOptions);

    public MaxioSubscriptionBillingService NewService()
    {
        var db = NewDbContext();
        var catalog = new MaxioPlanCatalog(Client, Settings, NullLogger<MaxioPlanCatalog>.Instance);
        var customers = new MaxioCustomerProvisioner(Client, db, TimeProvider.System, NullLogger<MaxioCustomerProvisioner>.Instance);
        return new MaxioSubscriptionBillingService(Client, catalog, customers, Settings, db, TimeProvider.System,
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    /// <summary>Catalog with both plans, no existing customer, customer create returns id 501, no subscriptions yet.</summary>
    public MaxioTestHarness WithNewCustomerHappyPath()
    {
        var reference = MaxioCustomerProvisioner.CustomerReferenceFor(UserName);
        Stub.On(HttpMethod.Get, ProductsPath, System.Net.HttpStatusCode.OK,
                MaxioStub.ProductsJson(("eshop-pro", "Pro Plan", 29900), ("basic-plan", "Basic Plan", 2900)))
            .On(HttpMethod.Get, CustomerLookupPath, System.Net.HttpStatusCode.NotFound, "{}")
            .On(HttpMethod.Post, CustomersPath, System.Net.HttpStatusCode.Created, MaxioStub.CustomerJson(501, reference))
            .On(HttpMethod.Get, CustomerSubscriptionsPath(501), System.Net.HttpStatusCode.OK, "[]");
        return this;
    }
}
