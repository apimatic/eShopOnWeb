using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;
using static Microsoft.eShopWeb.MaxioIntegrationTests.Fakes.FakeHttpMessageHandler;
using static Microsoft.eShopWeb.MaxioIntegrationTests.Fakes.MaxioBillingClientFactory;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Exercises <see cref="Infrastructure.Services.MaxioBillingClient"/> against a fake
/// <see cref="HttpClient"/> — the SDK's own documented test seam. Every response body below is a
/// real Maxio Advanced Billing wire shape, so these tests exercise the SDK's real JSON
/// deserialization and exception-throwing behaviour, not just method dispatch.
/// </summary>
public class MaxioBillingClientTests
{
    private const string ProductEshopPro = """
        { "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month", "require_credit_card": false, "request_credit_card": false } }
        """;

    private const string ProductBasicPlan = """
        { "product": { "id": 7126958, "name": "Basic Plan", "handle": "basic-plan", "price_in_cents": 2900, "interval": 1, "interval_unit": "month", "require_credit_card": false, "request_credit_card": false } }
        """;

    private const string ProductRequiresCreditCard = """
        { "product": { "id": 999, "name": "Card Required Plan", "handle": "card-plan", "price_in_cents": 1000, "interval": 1, "interval_unit": "month", "require_credit_card": true, "request_credit_card": true } }
        """;

    private const string MeteredComponent = """
        { "component": { "id": 3057195, "name": "API Calls", "handle": "api-call", "kind": "metered_component", "unit_name": "call" } }
        """;

    private const string NonMeteredComponent = """
        { "component": { "id": 4000, "name": "Seats", "handle": "seats", "kind": "quantity_based_component", "unit_name": "seat" } }
        """;

    private const string CustomerJaneDoe = """
        { "customer": { "id": 12345, "first_name": "Jane", "last_name": "Doe", "email": "jane.doe@example.com", "reference": "jane.doe@example.com" } }
        """;

    private const string ActiveSubscription = """
        {
          "subscription": {
            "id": 98765,
            "state": "active",
            "current_period_ends_at": "2026-08-01T00:00:00Z",
            "next_assessment_at": "2026-08-01T00:00:00Z",
            "cancel_at_end_of_period": false,
            "delayed_cancel_at": null,
            "customer": { "id": 12345, "reference": "jane.doe@example.com", "email": "jane.doe@example.com" },
            "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 }
          }
        }
        """;

    [Fact]
    public async Task ListPlansAsync_MapsMoneyMagnitudeCorrectly_ForBothConfiguredPlans()
    {
        var handler = new FakeHttpMessageHandler(Sequence(
            JsonResponse(HttpStatusCode.OK, ProductEshopPro),
            JsonResponse(HttpStatusCode.OK, ProductBasicPlan)));
        var client = Create(handler);

        var plans = await client.ListPlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(299.00m, plans[0].Price);
        // Regression guard: the SDK's IntervalUnit.ToString() does not reliably yield the wire
        // string ("month") — MapPlan must read .Value instead, or this becomes "IntervalUnit { Value = month }".
        Assert.Equal("month", plans[0].IntervalUnit);
        Assert.Equal("basic-plan", plans[1].Handle);
        Assert.Equal(29.00m, plans[1].Price);
    }

    [Fact]
    public async Task ValidateMeteredComponentAsync_ReturnsComponent_WhenKindIsMetered()
    {
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.OK, MeteredComponent)));
        var client = Create(handler);

        var component = await client.ValidateMeteredComponentAsync();

        Assert.True(component.IsMetered);
        Assert.Equal("api-call", component.Handle);
        Assert.Equal(3057195, component.Id);
    }

    [Fact]
    public async Task ValidateMeteredComponentAsync_ThrowsValidation_WhenKindIsNotMetered()
    {
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.OK, NonMeteredComponent)));
        var client = Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.ValidateMeteredComponentAsync());

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task FindCustomerAsync_ReturnsNull_WhenProviderReturns404()
    {
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.NotFound, "{}")));
        var client = Create(handler);

        var customer = await client.FindCustomerAsync("unknown@example.com");

        Assert.Null(customer);
    }

    [Fact]
    public async Task FindCustomerAsync_ReturnsMappedCustomer_WhenFound()
    {
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.OK, CustomerJaneDoe)));
        var client = Create(handler);

        var customer = await client.FindCustomerAsync("jane.doe@example.com");

        Assert.NotNull(customer);
        Assert.Equal(12345, customer!.Id);
        Assert.Equal("jane.doe@example.com", customer.Reference);
        Assert.Equal("jane.doe@example.com", customer.Email);
    }

    [Fact]
    public async Task EnsureCustomerAsync_CreatesCustomer_AndSendsSuppliedFields_WhenNoneExists()
    {
        var handler = new FakeHttpMessageHandler(Sequence(
            JsonResponse(HttpStatusCode.NotFound, "{}"),
            JsonResponse(HttpStatusCode.Created, CustomerJaneDoe)));
        var client = Create(handler);

        var customer = await client.EnsureCustomerAsync("jane.doe@example.com", "jane.doe@example.com", "Jane", "Doe");

        Assert.Equal(12345, customer.Id);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Contains("jane.doe@example.com", handler.Requests[1].Body);
        Assert.Contains("Jane", handler.Requests[1].Body);
        Assert.Contains("Doe", handler.Requests[1].Body);
    }

    [Fact]
    public async Task EnsureCustomerAsync_ReturnsExistingCustomer_WithoutCreatingANewOne_WhenAlreadyExists()
    {
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.OK, CustomerJaneDoe)));
        var client = Create(handler);

        var customer = await client.EnsureCustomerAsync("jane.doe@example.com", "jane.doe@example.com", "Jane", "Doe");

        Assert.Equal(12345, customer.Id);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ListCustomerSubscriptionsAsync_ReturnsEmptyList_WhenCustomerHasNoSubscriptions()
    {
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.OK, "[]")));
        var client = Create(handler);

        var subscriptions = await client.ListCustomerSubscriptionsAsync(12345);

        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ListCustomerSubscriptionsAsync_MapsEachSubscriptionInTheList()
    {
        var body = $"[{ActiveSubscription}]";
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.OK, body)));
        var client = Create(handler);

        var subscriptions = await client.ListCustomerSubscriptionsAsync(12345);

        Assert.Single(subscriptions);
        Assert.Equal(98765, subscriptions[0].Id);
        Assert.Equal("eshop-pro", subscriptions[0].ProductHandle);
        Assert.Equal(SubscriptionStatus.Active, subscriptions[0].Status);
        Assert.Equal(299.00m, subscriptions[0].Price);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_MapsSubscriptionAndMoney_OnHappyPath()
    {
        var handler = new FakeHttpMessageHandler(Sequence(
            JsonResponse(HttpStatusCode.OK, ProductEshopPro),
            JsonResponse(HttpStatusCode.Created, ActiveSubscription)));
        var client = Create(handler);

        var subscription = await client.CreateSubscriptionAsync(12345, "jane.doe@example.com", "eshop-pro");

        Assert.Equal(98765, subscription.Id);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(299.00m, subscription.Price);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.True(subscription.BlocksReEnrollment);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ThrowsValidation_WithoutCallingProvider_WhenProductRequiresCreditCard()
    {
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.OK, ProductRequiresCreditCard)));
        var client = Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.CreateSubscriptionAsync(12345, "jane.doe@example.com", "card-plan"));

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
        Assert.Single(handler.Requests); // only the product read — CreateSubscription must never be called
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ThrowsValidation_WhenProviderRejectsWith422()
    {
        var errorBody = """{ "errors": ["Customer has already subscribed to this product"] }""";
        var handler = new FakeHttpMessageHandler(Sequence(
            JsonResponse(HttpStatusCode.OK, ProductEshopPro),
            JsonResponse((HttpStatusCode)422, errorBody)));
        var client = Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.CreateSubscriptionAsync(12345, "jane.doe@example.com", "eshop-pro"));

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
        Assert.Contains("already subscribed", ex.Message);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ThrowsNotFound_ForUnknownId()
    {
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.NotFound, "{}")));
        var client = Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.GetSubscriptionAsync(999999));

        Assert.Equal(BillingErrorKind.NotFound, ex.Kind);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task RecordUsageAsync_ReturnsPeriodToDateTotal_OnHappyPath()
    {
        var usageResponse = """{ "usage": { "id": 777, "memo": "5 units", "quantity": 5, "component_id": 3057195, "subscription_id": 98765 } }""";
        var componentBalance = """{ "component": { "id": 3057195, "unit_balance": 137, "subscription_id": 98765 } }""";
        var handler = new FakeHttpMessageHandler(Sequence(
            JsonResponse(HttpStatusCode.OK, MeteredComponent),
            JsonResponse(HttpStatusCode.Created, usageResponse),
            JsonResponse(HttpStatusCode.OK, componentBalance)));
        var client = Create(handler);

        var result = await client.RecordUsageAsync(98765, 5, "5 units");

        Assert.Equal(777, result.UsageId);
        Assert.Equal(5, result.Quantity);
        Assert.Equal(137, result.PeriodToDateUnits);
    }

    [Fact]
    public async Task RecordUsageAsync_StillReturnsRecordedUsage_WhenPeriodToDateReadBackFails()
    {
        var usageResponse = """{ "usage": { "id": 777, "memo": null, "quantity": 5, "component_id": 3057195, "subscription_id": 98765 } }""";
        var handler = new FakeHttpMessageHandler(Sequence(
            JsonResponse(HttpStatusCode.OK, MeteredComponent),
            JsonResponse(HttpStatusCode.Created, usageResponse),
            JsonResponse(HttpStatusCode.InternalServerError, "{}")));
        var client = Create(handler);

        var result = await client.RecordUsageAsync(98765, 5, memo: null);

        Assert.Equal(777, result.UsageId);
        Assert.Equal(5, result.Quantity);
        Assert.Null(result.PeriodToDateUnits);
    }

    [Fact]
    public async Task RecordUsageAsync_ThrowsValidation_WithoutRecordingUsage_WhenComponentIsNotMetered()
    {
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.OK, NonMeteredComponent)));
        var client = Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.RecordUsageAsync(98765, 5, memo: null));

        Assert.Equal(BillingErrorKind.Validation, ex.Kind);
        Assert.Single(handler.Requests); // only the component lookup — CreateUsage must never be called
    }

    [Fact]
    public async Task AnyOperation_ThrowsConnectionFailure_WhenTheHttpCallItselfFails()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("simulated network failure"));
        var client = Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => client.ValidateMeteredComponentAsync());

        Assert.Equal(BillingErrorKind.ConnectionFailure, ex.Kind);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_MapsProrationMoneyCorrectly()
    {
        var previewResponse = """
            { "migration": { "prorated_adjustment_in_cents": -1000, "charge_in_cents": 2900, "payment_due_in_cents": 1900, "credit_applied_in_cents": 1000 } }
            """;
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.OK, previewResponse)));
        var client = Create(handler);

        var preview = await client.PreviewPlanChangeAsync(98765, "basic-plan");

        Assert.Equal("basic-plan", preview.TargetProductHandle);
        Assert.Equal(19.00m, preview.PaymentDue);
        Assert.Equal(10.00m, preview.CreditApplied);
    }

    private const string OnHoldSubscription = """
        { "subscription": { "id": 98765, "state": "on_hold", "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 } } }
        """;

    private const string CanceledSubscription = """
        { "subscription": { "id": 98765, "state": "canceled", "product": { "id": 7126957, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900 } } }
        """;

    [Fact]
    public async Task PauseSubscriptionAsync_ReturnsRefreshedState_OnHappyPath()
    {
        var handler = new FakeHttpMessageHandler(Sequence(
            JsonResponse(HttpStatusCode.OK, "{}"),
            JsonResponse(HttpStatusCode.OK, OnHoldSubscription)));
        var client = Create(handler);

        var subscription = await client.PauseSubscriptionAsync(98765);

        Assert.Equal(SubscriptionStatus.OnHold, subscription.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ResumeSubscriptionAsync_ReturnsRefreshedState_OnHappyPath()
    {
        var handler = new FakeHttpMessageHandler(Sequence(
            JsonResponse(HttpStatusCode.OK, "{}"),
            JsonResponse(HttpStatusCode.OK, ActiveSubscription)));
        var client = Create(handler);

        var subscription = await client.ResumeSubscriptionAsync(98765);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_Immediate_ReturnsRefreshedState()
    {
        var handler = new FakeHttpMessageHandler(Sequence(
            JsonResponse(HttpStatusCode.OK, "{}"),
            JsonResponse(HttpStatusCode.OK, CanceledSubscription)));
        var client = Create(handler);

        var subscription = await client.CancelSubscriptionAsync(98765, endOfPeriod: false);

        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_EndOfPeriod_ReturnsRefreshedState()
    {
        var handler = new FakeHttpMessageHandler(Sequence(
            JsonResponse(HttpStatusCode.OK, "{}"),
            JsonResponse(HttpStatusCode.OK, ActiveSubscription)));
        var client = Create(handler);

        var subscription = await client.CancelSubscriptionAsync(98765, endOfPeriod: true);

        // Delayed cancellation keeps the subscription Active with CancelAtEndOfPeriod set, until the period ends.
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task ReactivateSubscriptionAsync_ReturnsRefreshedState_OnHappyPath()
    {
        var handler = new FakeHttpMessageHandler(Sequence(
            JsonResponse(HttpStatusCode.OK, "{}"),
            JsonResponse(HttpStatusCode.OK, ActiveSubscription)));
        var client = Create(handler);

        var subscription = await client.ReactivateSubscriptionAsync(98765);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task CommitPlanChangeNowAsync_ReturnsUpdatedSubscription()
    {
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.OK, ActiveSubscription)));
        var client = Create(handler);

        var subscription = await client.CommitPlanChangeNowAsync(98765, "eshop-pro");

        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(299.00m, subscription.Price);
    }

    [Fact]
    public async Task SchedulePlanChangeAtRenewalAsync_ReturnsUpdatedSubscription()
    {
        var handler = new FakeHttpMessageHandler(Sequence(JsonResponse(HttpStatusCode.OK, ActiveSubscription)));
        var client = Create(handler);

        var subscription = await client.SchedulePlanChangeAtRenewalAsync(98765, "eshop-pro");

        Assert.Equal("eshop-pro", subscription.ProductHandle);
    }
}
