using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Covers the subscribe flow's contract: one customer per user, one subscription per plan, and a
/// well behaved answer when the billing system pushes back.
/// </summary>
public class MaxioSubscribeTests
{
    private static readonly Subscriber Demo = new("demouser@microsoft.com", "demouser@microsoft.com");

    private const string CustomerLookupPath = "/customers/lookup.json";
    private const string CustomersPath = "/customers.json";
    private const string SubscriptionsPath = "/subscriptions.json";
    private const string SubscriptionLookupPath = "/subscriptions/lookup.json";
    private const string CustomerSubscriptionsPath = "/customers/900/subscriptions.json";

    [Fact]
    public async Task Subscribe_creates_the_customer_and_the_subscription_on_first_use()
    {
        var host = new MaxioTestHost().WithStandardCatalog();
        host.Transport
            .EnqueueGet(CustomerLookupPath, MaxioTestHost.NotFound())
            .OnGet(CustomerLookupPath, Customer())
            .OnPost(CustomersPath, MaxioTestHost.Json(CustomerBody, HttpStatusCode.Created))
            .OnGet(CustomerSubscriptionsPath, MaxioTestHost.Json("[]"))
            .OnPost(SubscriptionsPath, MaxioTestHost.Json(SubscriptionBody(), HttpStatusCode.Created));

        var result = await host.Service.SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro"));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(5000, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.True(result.Subscription.GrantsEntitlement);
        Assert.Equal("eshop-pro", result.Subscription.PlanHandle);
        Assert.Equal(299.00m, result.Subscription.Price);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero), result.Subscription.NextBillingAt);
        Assert.Equal(1, host.Transport.CountOf(HttpMethod.Post, CustomersPath));
        Assert.Equal(1, host.Transport.CountOf(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task Subscribe_sends_a_collection_method_that_does_not_need_a_payment_profile()
    {
        var host = new MaxioTestHost().WithStandardCatalog();
        ArrangeExistingCustomerWithNoSubscriptions(host);
        host.Transport.OnPost(SubscriptionsPath, MaxioTestHost.Json(SubscriptionBody(), HttpStatusCode.Created));

        await host.Service.SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro"));

        var body = host.Transport.LastCall(HttpMethod.Post, SubscriptionsPath)!.Body;
        // The site advertises Relationship Invoicing, so the shopper is invoiced rather than
        // charged - otherwise a priced plan would be refused for having no card on file.
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":900", body);
    }

    [Fact]
    public async Task Subscribe_does_not_enroll_a_shopper_twice_in_a_plan_they_already_hold()
    {
        var host = new MaxioTestHost().WithStandardCatalog();
        host.Transport
            .OnGet(CustomerLookupPath, Customer())
            .OnGet(CustomerSubscriptionsPath, MaxioTestHost.Json(SubscriptionList(SubscriptionBody(bare: true))));

        var result = await host.Service.SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro"));

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(5000, result.Subscription.Id);
        Assert.Equal(0, host.Transport.CountOf(HttpMethod.Post, SubscriptionsPath));
        Assert.Equal(0, host.Transport.CountOf(HttpMethod.Post, CustomersPath));
    }

    [Fact]
    public async Task Subscribe_re_reads_the_customer_when_another_writer_claimed_the_reference_first()
    {
        // The cross-process race: our lookup missed, then a concurrent request created the
        // customer before our POST landed. Adopting the winner is what keeps it to one customer.
        var host = new MaxioTestHost().WithStandardCatalog();
        host.Transport
            .EnqueueGet(CustomerLookupPath, MaxioTestHost.NotFound())
            .OnGet(CustomerLookupPath, Customer())
            .OnPost(CustomersPath, MaxioTestHost.ReferenceTaken())
            .OnGet(CustomerSubscriptionsPath, MaxioTestHost.Json("[]"))
            .OnPost(SubscriptionsPath, MaxioTestHost.Json(SubscriptionBody(), HttpStatusCode.Created));

        var result = await host.Service.SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro"));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(900, result.Subscription.CustomerId);
        Assert.Equal(1, host.Transport.CountOf(HttpMethod.Post, CustomersPath));
    }

    [Fact]
    public async Task Subscribe_adopts_the_winner_when_a_concurrent_request_took_the_subscription_reference()
    {
        // Same race one level down, and the guarantee the in-process lock cannot give: two hosts
        // derive the same reference, the billing system rejects the loser, and the loser returns
        // the subscription that won instead of failing or duplicating.
        var host = new MaxioTestHost().WithStandardCatalog();
        ArrangeExistingCustomerWithNoSubscriptions(host);
        host.Transport
            .OnPost(SubscriptionsPath, MaxioTestHost.ReferenceTaken())
            .OnGet(SubscriptionLookupPath, MaxioTestHost.Json(SubscriptionBody()));

        var result = await host.Service.SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro"));

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(5000, result.Subscription.Id);
        Assert.Equal(1, host.Transport.CountOf(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task Subscribe_reuses_one_idempotency_key_for_one_subscription()
    {
        var host = new MaxioTestHost().WithStandardCatalog();
        ArrangeExistingCustomerWithNoSubscriptions(host);
        host.Transport.OnGet(SubscriptionLookupPath, MaxioTestHost.Json(SubscriptionBody()));

        var result = await host.Service.SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro", "checkout-123"));

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(0, host.Transport.CountOf(HttpMethod.Post, SubscriptionsPath));

        var lookup = host.Transport.LastCall(HttpMethod.Get, SubscriptionLookupPath)!;
        Assert.Contains("checkout-123", Uri.UnescapeDataString(lookup.Query));
    }

    [Fact]
    public async Task Subscribe_numbers_a_repeat_signup_so_concurrent_callers_collide_but_a_resubscribe_does_not()
    {
        var host = new MaxioTestHost().WithStandardCatalog();
        host.Transport
            .OnGet(CustomerLookupPath, Customer())
            // One cancelled subscription to the plan already exists, so this is a genuine
            // re-subscribe rather than a duplicate.
            .OnGet(CustomerSubscriptionsPath, MaxioTestHost.Json(SubscriptionList(SubscriptionBody(bare: true, state: "canceled"))))
            .OnPost(SubscriptionsPath, MaxioTestHost.Json(SubscriptionBody(id: 5001), HttpStatusCode.Created));

        var result = await host.Service.SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro"));

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        var body = host.Transport.LastCall(HttpMethod.Post, SubscriptionsPath)!.Body;
        Assert.Contains("\"reference\":\"eshop:sub:demouser@microsoft.com:eshop-pro:2\"", body);
    }

    [Fact]
    public async Task Subscribe_rejects_a_plan_the_catalog_does_not_publish()
    {
        var host = new MaxioTestHost().WithStandardCatalog();

        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => host.Service.SubscribeAsync(new SubscribeRequest(Demo, "gold-plan")));

        Assert.Equal("gold-plan", exception.RequestedHandle);
        Assert.Contains("eshop-pro", exception.AvailableHandles);
        Assert.Equal(0, host.Transport.CountOf(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task Subscribe_refuses_a_plan_that_needs_a_stored_payment_method()
    {
        var host = new MaxioTestHost();
        host.Transport
            .OnGet("/site.json", MaxioTestHost.Json("""
                {"site":{"id":1,"subdomain":"test-site","currency":"USD","relationship_invoicing_enabled":true}}
                """))
            .OnGet("/product_families.json", MaxioTestHost.Json("""
                [{"product_family":{"id":42,"handle":"eshop-subscribe","name":"eShopSubscribe"}}]
                """))
            .OnGet("/product_families/42/products.json", MaxioTestHost.Json("""
                [{"product":{"id":700,"handle":"card-required","name":"Card Required","price_in_cents":1000,
                  "interval":1,"interval_unit":"month","require_credit_card":true,
                  "product_family":{"id":42,"handle":"eshop-subscribe"}}}]
                """));

        await Assert.ThrowsAsync<SubscriptionNotAllowedException>(
            () => host.Service.SubscribeAsync(new SubscribeRequest(Demo, "card-required")));

        Assert.Equal(0, host.Transport.CountOf(HttpMethod.Post, SubscriptionsPath));
    }

    [Fact]
    public async Task Subscribe_surfaces_a_billing_outage_as_a_transient_provider_failure()
    {
        var host = new MaxioTestHost();
        host.Transport.OnGet("/site.json", MaxioTestHost.Json("{}", HttpStatusCode.ServiceUnavailable));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => host.Service.SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro")));

        Assert.True(exception.IsTransient);
        Assert.Equal(503, exception.ProviderStatusCode);
    }

    [Fact]
    public async Task Subscribe_reports_a_rejected_api_key_as_a_configuration_problem()
    {
        var host = new MaxioTestHost();
        host.Transport.OnGet("/site.json", MaxioTestHost.Json("""{"errors":["Unauthorized"]}""", HttpStatusCode.Unauthorized));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => host.Service.SubscribeAsync(new SubscribeRequest(Demo, "eshop-pro")));

        Assert.False(exception.IsTransient);
        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    [Fact]
    public async Task Subscribe_requires_a_plan_when_no_default_is_configured()
    {
        var host = new MaxioTestHost().WithStandardCatalog();

        var exception = await Assert.ThrowsAsync<SubscriptionPlanNotFoundException>(
            () => host.Service.SubscribeAsync(new SubscribeRequest(Demo, null)));

        Assert.Null(exception.RequestedHandle);
    }

    [Fact]
    public async Task Subscribe_falls_back_to_the_configured_default_plan()
    {
        var host = new MaxioTestHost(new MaxioSettingsBuilder().WithDefaultPlan("basic-plan").Build()).WithStandardCatalog();
        ArrangeExistingCustomerWithNoSubscriptions(host);
        host.Transport.OnPost(SubscriptionsPath, MaxioTestHost.Json(SubscriptionBody(), HttpStatusCode.Created));

        await host.Service.SubscribeAsync(new SubscribeRequest(Demo, null));

        Assert.Contains("\"product_handle\":\"basic-plan\"", host.Transport.LastCall(HttpMethod.Post, SubscriptionsPath)!.Body);
    }

    private static void ArrangeExistingCustomerWithNoSubscriptions(MaxioTestHost host)
    {
        host.Transport
            .OnGet(CustomerLookupPath, Customer())
            .OnGet(CustomerSubscriptionsPath, MaxioTestHost.Json("[]"));
    }

    private static HttpResponseMessage Customer() => MaxioTestHost.Json(CustomerBody);

    private const string CustomerBody = """
        {"customer":{"id":900,"reference":"eshop:cust:demouser@microsoft.com","email":"demouser@microsoft.com",
         "first_name":"demouser","last_name":"Subscriber","created_at":"2026-09-01T00:00:00+00:00"}}
        """;

    private static string SubscriptionList(params string[] subscriptions) =>
        "[" + string.Join(",", subscriptions.Select(s => "{\"subscription\":" + s + "}")) + "]";

    /// <summary>
    /// A subscription exactly as Advanced Billing returns it. <paramref name="bare"/> selects the
    /// unwrapped object used inside a list, rather than the single-resource envelope.
    /// </summary>
    private static string SubscriptionBody(bool bare = false, int id = 5000, string state = "active")
    {
        const string template = """
            {"id":<ID>,"state":"<STATE>","reference":"eshop:sub:demouser@microsoft.com:eshop-pro:1",
             "balance_in_cents":29900,"product_price_in_cents":29900,
             "current_period_started_at":"2026-09-06T00:00:00+00:00",
             "current_period_ends_at":"2026-10-06T00:00:00+00:00",
             "next_assessment_at":"2026-10-06T00:00:00+00:00",
             "activated_at":"2026-09-06T00:00:00+00:00","created_at":"2026-09-06T00:00:00+00:00",
             "payment_collection_method":"remittance","currency":"USD",
             "customer":{"id":900,"reference":"eshop:cust:demouser@microsoft.com","email":"demouser@microsoft.com"},
             "product":{"id":700,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}
            """;

        var subscription = template
            .Replace("<ID>", id.ToString())
            .Replace("<STATE>", state);

        return bare ? subscription : "{\"subscription\":" + subscription + "}";
    }
}
