using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MaxioSubscriptionServiceTests
{
    private const string PlanJson =
        """{"id":10,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"request_credit_card":false}""";

    private static (MaxioSubscriptionService Service, QueueStubHandler Handler) CreateService(params (HttpStatusCode Status, string Json)[] responses)
    {
        var handler = new QueueStubHandler(responses);
        var httpClient = new HttpClient(handler);
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            // Zero delay / single attempt so a scripted error response resolves immediately in tests.
            Retry = RetryOptions.Default() with { MaxRetries = 1, Delay = TimeSpan.Zero, UseExponentialBackoff = false }
        };
        var client = new MaxioAdvancedBillingClient(httpClient, options);
        var service = new MaxioSubscriptionService(
            client,
            Options.Create(new MaxioOptions { ProductFamilyHandle = "eshop-subscribe" }),
            NullLogger<MaxioSubscriptionService>.Instance);
        return (service, handler);
    }

    [TestMethod]
    public async Task GetAvailablePlansAsync_ReturnsMappedPlans()
    {
        var (service, _) = CreateService(
            (HttpStatusCode.OK, """[{"product_family":{"id":1,"name":"eShop Subscribe","handle":"eshop-subscribe"}}]"""),
            (HttpStatusCode.OK, "[{\"product\":" + PlanJson + "}]"));

        var plans = await service.GetAvailablePlansAsync();

        Assert.AreEqual(1, plans.Count);
        Assert.AreEqual("eshop-pro", plans[0].Handle);
        Assert.AreEqual("Pro Plan", plans[0].Name);
        Assert.AreEqual(299m, plans[0].PriceAmount);
        Assert.AreEqual(1, plans[0].Interval);
        Assert.AreEqual("month", plans[0].IntervalUnit);
        Assert.IsFalse(plans[0].RequiresPaymentMethod);
    }

    [TestMethod]
    public async Task GetAvailablePlansAsync_UnknownFamilyHandle_ThrowsSubscriptionProviderException()
    {
        var (service, _) = CreateService(
            (HttpStatusCode.OK, """[{"product_family":{"id":1,"name":"Other","handle":"some-other-family"}}]"""));

        await Assert.ThrowsExceptionAsync<SubscriptionProviderException>(() => service.GetAvailablePlansAsync());
    }

    [TestMethod]
    public async Task SubscribeAsync_NewCustomer_CreatesCustomerAndSubscription()
    {
        var (service, handler) = CreateService(
            (HttpStatusCode.NotFound, "{}"),
            (HttpStatusCode.OK, """{"customer":{"id":5,"first_name":"jane","last_name":"Customer","email":"jane@example.com","reference":"user-1"}}"""),
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.OK, """{"subscription":{"id":100,"state":"active","reference":"eshop:user-1:eshop-pro","next_assessment_at":"2026-10-05T00:00:00Z","product":""" + PlanJson + "}}"));

        var result = await service.SubscribeAsync(
            new SubscriptionEnrollmentRequest("user-1", "jane@example.com", "jane", "Customer", "eshop-pro", 1, "month"));

        Assert.AreEqual(100, result.Id);
        Assert.AreEqual("eshop-pro", result.PlanHandle);
        Assert.AreEqual("active", result.State);
        Assert.AreEqual(299m, result.PriceAmount);
        Assert.AreEqual(4, handler.Requests.Count);
    }

    [TestMethod]
    public async Task SubscribeAsync_ExistingLiveSubscription_ReturnsItWithoutCreatingDuplicate()
    {
        var (service, handler) = CreateService(
            (HttpStatusCode.OK, """{"customer":{"id":5,"first_name":"jane","last_name":"Customer","email":"jane@example.com","reference":"user-1"}}"""),
            (HttpStatusCode.OK, """[{"subscription":{"id":100,"state":"active","reference":"eshop:user-1:eshop-pro","next_assessment_at":"2026-10-05T00:00:00Z","product":""" + PlanJson + "}}]"));

        var result = await service.SubscribeAsync(
            new SubscriptionEnrollmentRequest("user-1", "jane@example.com", "jane", "Customer", "eshop-pro", 1, "month"));

        Assert.AreEqual(100, result.Id);
        // Only the customer lookup + the subscription-list dedupe check ran - no CreateSubscription call.
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task SubscribeAsync_TerminalStateSubscriptionExists_CreatesNewOne()
    {
        var (service, handler) = CreateService(
            (HttpStatusCode.OK, """{"customer":{"id":5,"first_name":"jane","last_name":"Customer","email":"jane@example.com","reference":"user-1"}}"""),
            (HttpStatusCode.OK, """[{"subscription":{"id":99,"state":"canceled","reference":"eshop:user-1:eshop-pro-old","next_assessment_at":null,"product":""" + PlanJson + "}}]"),
            (HttpStatusCode.OK, """{"subscription":{"id":100,"state":"active","reference":"eshop:user-1:eshop-pro","next_assessment_at":"2026-10-05T00:00:00Z","product":""" + PlanJson + "}}"));

        var result = await service.SubscribeAsync(
            new SubscriptionEnrollmentRequest("user-1", "jane@example.com", "jane", "Customer", "eshop-pro", 1, "month"));

        Assert.AreEqual(100, result.Id);
        Assert.AreEqual(3, handler.Requests.Count);
    }

    [TestMethod]
    public async Task GetSubscriptionsForCustomerAsync_NoCustomerYet_ReturnsEmptyList()
    {
        var (service, _) = CreateService((HttpStatusCode.NotFound, "{}"));

        var result = await service.GetSubscriptionsForCustomerAsync("user-1");

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task GetSubscriptionsForCustomerAsync_ProviderError_ThrowsSubscriptionProviderException()
    {
        var (service, _) = CreateService((HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsExceptionAsync<SubscriptionProviderException>(
            () => service.GetSubscriptionsForCustomerAsync("user-1"));
    }
}
