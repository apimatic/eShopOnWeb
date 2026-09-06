using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// Builds a <see cref="MaxioSubscriptionBillingService"/> over a stubbed transport, with the same handler
/// pipeline the application registers — the single-send guard included, since several tests are about
/// exactly what that guard does.
/// </summary>
internal static class MaxioTestContext
{
    public const string FamilyHandle = "eshop-subscribe";
    public const string ProPlanHandle = "eshop-pro";
    public const string SubscriberEmail = "demouser@microsoft.com";

    public static MaxioSubscriptionBillingService BuildService(StubHttpMessageHandler handler,
        MaxioOptions? options = null)
    {
        options ??= new MaxioOptions
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle,
            DefaultPlanHandle = ProPlanHandle,
            // One attempt plus one retry: enough for a retry to be attempted at all, which is what the
            // write-once tests need to observe.
            MaxRetries = 1,
            AttemptTimeout = TimeSpan.FromSeconds(5),
            CallBudget = TimeSpan.FromSeconds(15)
        };

        var httpClient = new HttpClient(new MaxioSingleSendHandler { InnerHandler = handler });

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            BasicAuth = new BasicAuthCredentials { Username = options.ApiKey, Password = "x" },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = options.MaxRetries,
                Timeout = options.AttemptTimeout,
                Delay = TimeSpan.Zero,
                MaxJitter = TimeSpan.Zero
            }
        };
        clientOptions.Server.Production.Us.Site = options.Subdomain;

        var client = new MaxioAdvancedBillingClient(httpClient, clientOptions);

        return new MaxioSubscriptionBillingService(client, Options.Create(options),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    /// <summary>Stubs the catalog reads every flow starts with: site, product family, products.</summary>
    public static StubHttpMessageHandler WithCatalog(this StubHttpMessageHandler handler,
        int familyId = 4242, bool relationshipInvoicing = true)
    {
        var siteJson = "{\"site\":{\"id\":1,\"currency\":\"USD\",\"relationship_invoicing_enabled\":"
            + (relationshipInvoicing ? "true" : "false")
            + ",\"default_payment_collection_method\":\"automatic\"}}";

        var familiesJson = "["
            + "{\"product_family\":{\"id\":999,\"handle\":\"some-other-family\",\"name\":\"Other\"}},"
            + "{\"product_family\":{\"id\":" + familyId + ",\"handle\":\"" + FamilyHandle
            + "\",\"name\":\"eShop Subscribe\"}}]";

        return handler
            .On(HttpMethod.Get, "/site.json", HttpStatusCode.OK, siteJson)
            .On(HttpMethod.Get, "/product_families.json", HttpStatusCode.OK, familiesJson)
            .On(HttpMethod.Get, $"/product_families/{familyId}/products.json", HttpStatusCode.OK,
                """
                [
                  {"product":{"id":11,"handle":"eshop-pro","name":"Pro Plan","description":"Everything",
                    "price_in_cents":29900,"interval":1,"interval_unit":"month",
                    "initial_charge_in_cents":0,"trial_interval":0,"trial_price_in_cents":0,
                    "require_credit_card":false,"archived_at":null}},
                  {"product":{"id":12,"handle":"basic-plan","name":"Basic Plan",
                    "price_in_cents":2900,"interval":1,"interval_unit":"month",
                    "require_credit_card":false,"archived_at":null}}
                ]
                """);
    }

    public static string CustomerJson(int id = 555) =>
        "{\"customer\":{\"id\":" + id
        + ",\"reference\":\"eshoponweb-demouser@microsoft.com\",\"email\":\"demouser@microsoft.com\""
        + ",\"first_name\":\"Demouser\",\"last_name\":\"Customer\"}}";

    public static string SubscriptionJson(int id = 94212077, string state = "active",
        string handle = ProPlanHandle) =>
        "{\"subscription\":{\"id\":" + id + ",\"state\":\"" + state + "\","
        + "\"product\":{\"id\":11,\"handle\":\"" + handle
        + "\",\"name\":\"Pro Plan\",\"price_in_cents\":29900},"
        + "\"product_price_in_cents\":29900,\"total_revenue_in_cents\":0,\"currency\":\"USD\","
        + "\"payment_collection_method\":\"remittance\","
        + "\"current_period_started_at\":\"2026-09-06T16:11:53Z\","
        + "\"current_period_ends_at\":\"2026-10-06T16:11:53Z\","
        + "\"next_assessment_at\":\"2026-10-06T16:11:53Z\","
        + "\"created_at\":\"2026-09-06T16:11:53Z\"}}";

    public static string SubscriptionListJson(params string[] subscriptionObjects) =>
        "[" + string.Join(",", subscriptionObjects) + "]";
}
