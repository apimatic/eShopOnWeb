using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

public class MaxioCatalogAndListingTests
{
    private static readonly Subscriber Demo = new("demouser@microsoft.com", "demouser@microsoft.com");

    [Fact]
    public async Task Plans_are_projected_from_the_configured_product_family_cheapest_first()
    {
        var host = new MaxioTestHost().WithStandardCatalog();

        var plans = await host.Service.ListPlansAsync();

        Assert.Collection(
            plans,
            basic =>
            {
                Assert.Equal("basic-plan", basic.Handle);
                Assert.Equal(29.00m, basic.Price);
                Assert.Equal("USD", basic.Currency);
            },
            pro =>
            {
                Assert.Equal("eshop-pro", pro.Handle);
                Assert.Equal(299.00m, pro.Price);
                Assert.Equal("month", pro.IntervalUnit);
                Assert.False(pro.RequiresPaymentMethod);
            });
    }

    [Fact]
    public async Task The_product_family_is_resolved_by_handle_not_by_a_hard_coded_id()
    {
        // Numeric ids are reassigned when a catalog is re-seeded; the handle is the stable key.
        var host = new MaxioTestHost();
        host.Transport
            .OnGet("/site.json", MaxioTestHost.Json("""{"site":{"id":1,"currency":"USD","relationship_invoicing_enabled":true}}"""))
            .OnGet("/product_families.json", MaxioTestHost.Json("""
                [{"product_family":{"id":11,"handle":"other-family"}},
                 {"product_family":{"id":42,"handle":"eshop-subscribe"}}]
                """))
            .OnGet("/product_families/42/products.json", MaxioTestHost.Json("""
                [{"product":{"id":700,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
                  "interval":1,"interval_unit":"month","product_family":{"id":42,"handle":"eshop-subscribe"}}}]
                """));

        var plans = await host.Service.ListPlansAsync();

        Assert.Equal("eshop-pro", Assert.Single(plans).Handle);
    }

    [Fact]
    public async Task A_missing_product_family_is_reported_against_the_setting_that_selects_it()
    {
        var host = new MaxioTestHost();
        host.Transport
            .OnGet("/site.json", MaxioTestHost.Json("""{"site":{"id":1,"subdomain":"test-site","currency":"USD"}}"""))
            .OnGet("/product_families.json", MaxioTestHost.Json("[]"));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => host.Service.ListPlansAsync());

        Assert.Contains("Maxio:ProductFamilyHandle", exception.Message);
    }

    [Fact]
    public async Task Archived_plans_are_not_published()
    {
        var host = new MaxioTestHost();
        host.Transport
            .OnGet("/site.json", MaxioTestHost.Json("""{"site":{"id":1,"currency":"USD","relationship_invoicing_enabled":true}}"""))
            .OnGet("/product_families.json", MaxioTestHost.Json("""[{"product_family":{"id":42,"handle":"eshop-subscribe"}}]"""))
            .OnGet("/product_families/42/products.json", MaxioTestHost.Json("""
                [{"product":{"id":700,"handle":"retired","name":"Retired","price_in_cents":100,"interval":1,
                  "interval_unit":"month","archived_at":"2026-01-01T00:00:00+00:00","product_family":{"id":42,"handle":"eshop-subscribe"}}}]
                """));

        Assert.Empty(await host.Service.ListPlansAsync());
    }

    [Fact]
    public async Task A_shopper_who_never_subscribed_has_an_empty_list_rather_than_an_error()
    {
        var host = new MaxioTestHost();
        host.Transport.OnGet("/customers/lookup.json", MaxioTestHost.NotFound());

        Assert.Empty(await host.Service.ListSubscriptionsAsync(Demo));
    }

    [Fact]
    public async Task Subscriptions_are_listed_newest_first_with_the_next_billing_date()
    {
        var host = new MaxioTestHost();
        host.Transport
            .OnGet("/customers/lookup.json", MaxioTestHost.Json("""
                {"customer":{"id":900,"reference":"eshop:cust:demouser@microsoft.com","email":"demouser@microsoft.com"}}
                """))
            .OnGet("/customers/900/subscriptions.json", MaxioTestHost.Json("""
                [{"subscription":{"id":1,"state":"canceled","created_at":"2026-01-01T00:00:00+00:00",
                   "product_price_in_cents":2900,"currency":"USD",
                   "product":{"id":701,"handle":"basic-plan","name":"Basic Plan","interval":1,"interval_unit":"month"}}},
                 {"subscription":{"id":2,"state":"active","created_at":"2026-09-01T00:00:00+00:00",
                   "product_price_in_cents":29900,"currency":"USD",
                   "current_period_ends_at":"2026-10-01T00:00:00+00:00",
                   "next_assessment_at":"2026-10-01T00:00:00+00:00",
                   "product":{"id":700,"handle":"eshop-pro","name":"Pro Plan","interval":1,"interval_unit":"month"}}}]
                """));

        var subscriptions = await host.Service.ListSubscriptionsAsync(Demo);

        Assert.Equal(new[] { 2, 1 }, subscriptions.Select(s => s.Id));
        Assert.True(subscriptions[0].GrantsEntitlement);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), subscriptions[0].NextBillingAt);
        Assert.False(subscriptions[1].IsLive);
    }

    [Fact]
    public async Task A_retryable_read_failure_is_retried_before_it_is_reported()
    {
        var host = new MaxioTestHost(new MaxioSettingsBuilder().WithRetries(2).Build());
        host.Transport
            .EnqueueGet("/site.json", MaxioTestHost.Json("{}", HttpStatusCode.BadGateway))
            .OnGet("/site.json", MaxioTestHost.Json("""{"site":{"id":1,"currency":"USD","relationship_invoicing_enabled":true}}"""))
            .OnGet("/product_families.json", MaxioTestHost.Json("""[{"product_family":{"id":42,"handle":"eshop-subscribe"}}]"""))
            .OnGet("/product_families/42/products.json", MaxioTestHost.Json("[]"));

        await host.Service.ListPlansAsync();

        Assert.Equal(2, host.Transport.CountOf(HttpMethod.Get, "/site.json"));
    }
}
