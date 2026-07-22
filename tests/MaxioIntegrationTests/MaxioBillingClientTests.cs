using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Drives <c>MaxioBillingClient</c> against canned provider responses, exercising the real SDK
/// serialization and error paths. These assert the behaviour the storefront depends on: correct money
/// magnitudes, correct not-found handling, and provider failures surfaced as typed exceptions.
/// </summary>
public class MaxioBillingClientTests
{
    // ---------- Catalog reads ----------

    [Fact]
    public async Task ListPlansAsync_converts_prices_from_cents_to_whole_currency_units()
    {
        var handler = StubHttpMessageHandler.Sequence(MaxioPayloads.ProductFamilies(), MaxioPayloads.ProductList());
        var client = MaxioTestContext.CreateClient(handler);

        var plans = (await client.ListPlansAsync()).ToList();

        Assert.Equal(2, plans.Count);

        var pro = plans.Single(plan => plan.Handle == MaxioTestContext.PRO_HANDLE);
        Assert.Equal(299.00m, pro.Price);
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(1, pro.Interval);
        Assert.Equal(BillingIntervalUnit.Month, pro.IntervalUnit);
        Assert.Equal("month", pro.BillingPeriodDescription);
        Assert.False(pro.RequiresPaymentMethod);
        Assert.False(pro.IsArchived);

        var basic = plans.Single(plan => plan.Handle == MaxioTestContext.BASIC_HANDLE);
        Assert.Equal(29.00m, basic.Price);
    }

    [Fact]
    public async Task ListPlansAsync_returns_an_empty_list_when_the_family_has_no_products()
    {
        var handler = StubHttpMessageHandler.Sequence(MaxioPayloads.ProductFamilies(), MaxioPayloads.EmptyProductList());
        var client = MaxioTestContext.CreateClient(handler);

        var plans = await client.ListPlansAsync();

        Assert.Empty(plans);
    }

    [Fact]
    public async Task ListPlansAsync_reports_a_configuration_error_when_the_configured_family_does_not_exist()
    {
        var handler = StubHttpMessageHandler.Sequence(MaxioPayloads.ProductFamilies("some-other-family"));
        var client = MaxioTestContext.CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.ListPlansAsync());

        Assert.Contains(MaxioTestContext.FAMILY_HANDLE, exception.Message);
    }

    [Fact]
    public async Task ListPlansAsync_reports_a_configuration_error_when_no_families_exist_at_all()
    {
        var handler = StubHttpMessageHandler.Sequence(MaxioPayloads.EmptyProductFamilies());
        var client = MaxioTestContext.CreateClient(handler);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => client.ListPlansAsync());
    }

    [Fact]
    public async Task ListPlansAsync_resolves_the_family_handle_only_once_per_client()
    {
        var handler = StubHttpMessageHandler.Sequence(MaxioPayloads.ProductFamilies(), MaxioPayloads.ProductList());
        var client = MaxioTestContext.CreateClient(handler);

        await client.ListPlansAsync();
        await client.ListPlansAsync();

        // One family lookup, then one product list per call.
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_returns_the_plan_with_its_price_in_whole_currency_units()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.ProProduct());
        var client = MaxioTestContext.CreateClient(handler);

        var plan = await client.FindPlanByHandleAsync(MaxioTestContext.PRO_HANDLE);

        Assert.NotNull(plan);
        Assert.Equal(MaxioPayloads.PRO_ID, plan!.Id);
        Assert.Equal(299.00m, plan.Price);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_returns_null_for_an_unknown_handle()
    {
        var handler = StubHttpMessageHandler.Failing(HttpStatusCode.NotFound);
        var client = MaxioTestContext.CreateClient(handler);

        var plan = await client.FindPlanByHandleAsync("no-such-plan");

        Assert.Null(plan);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_returns_null_for_a_blank_handle_without_calling_the_provider()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.ProProduct());
        var client = MaxioTestContext.CreateClient(handler);

        var plan = await client.FindPlanByHandleAsync("   ");

        Assert.Null(plan);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_marks_an_archived_plan_as_archived()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.ProProduct(archivedAt: "2026-01-01T00:00:00-05:00"));
        var client = MaxioTestContext.CreateClient(handler);

        var plan = await client.FindPlanByHandleAsync(MaxioTestContext.PRO_HANDLE);

        Assert.True(plan!.IsArchived);
    }

    [Fact]
    public async Task FindPlanByHandleAsync_surfaces_a_provider_failure_as_a_typed_exception()
    {
        var handler = StubHttpMessageHandler.Failing(HttpStatusCode.Unauthorized, "bad credentials");
        var client = MaxioTestContext.CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.FindPlanByHandleAsync(MaxioTestContext.PRO_HANDLE));

        Assert.Equal("ReadProductByHandle", exception.Operation);
        Assert.Equal(401, exception.StatusCode);
        Assert.Contains("bad credentials", exception.ProviderMessage);
    }

    // ---------- Metered component ----------

    [Fact]
    public async Task GetMeteredComponentAsync_reads_a_one_cent_unit_price_from_the_decimal_string_field()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.MeteredComponent(unitPrice: "0.01"));
        var client = MaxioTestContext.CreateClient(handler);

        var component = await client.GetMeteredComponentAsync();

        Assert.NotNull(component);
        Assert.Equal(0.01m, component!.UnitPrice);
        Assert.True(component.IsMetered);
        Assert.Equal("call", component.UnitName);
        Assert.Equal(MaxioPayloads.COMPONENT_ID, component.Id);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_falls_back_to_the_cents_field_when_no_decimal_string_is_sent()
    {
        var handler = StubHttpMessageHandler.Returning(
            MaxioPayloads.MeteredComponent(unitPrice: null, pricePerUnitInCents: 1));
        var client = MaxioTestContext.CreateClient(handler);

        var component = await client.GetMeteredComponentAsync();

        // One cent must stay one cent — not one dollar.
        Assert.Equal(0.01m, component!.UnitPrice);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_reports_a_component_of_the_wrong_kind_as_not_metered()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.MeteredComponent(kind: "quantity_based_component"));
        var client = MaxioTestContext.CreateClient(handler);

        var component = await client.GetMeteredComponentAsync();

        Assert.NotNull(component);
        Assert.False(component!.IsMetered);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_returns_null_when_the_handle_does_not_resolve()
    {
        var handler = StubHttpMessageHandler.Failing(HttpStatusCode.NotFound);
        var client = MaxioTestContext.CreateClient(handler);

        Assert.Null(await client.GetMeteredComponentAsync());
    }

    [Fact]
    public async Task GetMeteredComponentAsync_refuses_a_component_that_lives_on_another_product_family()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.MeteredComponent(familyHandle: "other-family"));
        var client = MaxioTestContext.CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(() => client.GetMeteredComponentAsync());

        Assert.Contains("other-family", exception.Message);
    }

    [Fact]
    public async Task GetMeteredComponentAsync_reports_a_configuration_error_when_no_handle_is_configured()
    {
        var settings = MaxioTestContext.Settings();
        settings.MeteredComponentHandle = string.Empty;
        var client = MaxioTestContext.CreateClient(StubHttpMessageHandler.Returning("{}"), settings);

        await Assert.ThrowsAsync<BillingConfigurationException>(() => client.GetMeteredComponentAsync());
    }

    // ---------- Customers ----------

    [Fact]
    public async Task FindCustomerByReferenceAsync_returns_the_customer_for_a_known_reference()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Customer());
        var client = MaxioTestContext.CreateClient(handler);

        var customer = await client.FindCustomerByReferenceAsync(MaxioPayloads.CUSTOMER_REFERENCE);

        Assert.NotNull(customer);
        Assert.Equal(MaxioPayloads.CUSTOMER_ID, customer!.Id);
        Assert.Equal(MaxioPayloads.CUSTOMER_REFERENCE, customer.Reference);
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_returns_null_for_an_unknown_reference()
    {
        var handler = StubHttpMessageHandler.Failing(HttpStatusCode.NotFound);
        var client = MaxioTestContext.CreateClient(handler);

        Assert.Null(await client.FindCustomerByReferenceAsync("nobody@example.com"));
    }

    [Fact]
    public async Task CreateCustomerAsync_sends_the_stable_reference_that_makes_subscribe_idempotent()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Customer());
        var client = MaxioTestContext.CreateClient(handler);

        var customer = await client.CreateCustomerAsync(
            new NewBillingCustomer(MaxioPayloads.CUSTOMER_REFERENCE, MaxioPayloads.CUSTOMER_REFERENCE, "Demo", "User"));

        Assert.Equal(MaxioPayloads.CUSTOMER_ID, customer.Id);

        var body = Assert.Single(handler.Requests).Body;
        Assert.NotNull(body);
        Assert.Contains("\"reference\"", body);
        Assert.Contains(MaxioPayloads.CUSTOMER_REFERENCE, body);
        Assert.Contains("\"customer\"", body);
    }

    [Fact]
    public async Task CreateCustomerAsync_surfaces_a_provider_rejection_as_a_typed_exception()
    {
        // The provider's real 422 body for a customer does not match the shape its own generated error
        // model expects, so this also pins that no raw deserialization failure escapes the seam.
        var handler = StubHttpMessageHandler.Failing(HttpStatusCode.UnprocessableEntity, MaxioPayloads.ErrorList("Email is invalid"));
        var client = MaxioTestContext.CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateCustomerAsync(new NewBillingCustomer("ref", "bad", "Demo", "User")));

        Assert.Equal("CreateCustomer", exception.Operation);
    }

    [Fact]
    public async Task A_response_the_sdk_cannot_parse_is_reported_as_a_billing_provider_failure()
    {
        var handler = StubHttpMessageHandler.Returning("{\"subscription\":\"not-an-object\"}");
        var client = MaxioTestContext.CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.GetSubscriptionAsync(MaxioPayloads.SUBSCRIPTION_ID));

        Assert.Equal("ReadSubscription", exception.Operation);
        Assert.Contains("could not be interpreted", exception.ProviderMessage);
    }

    // ---------- Subscriptions ----------

    [Fact]
    public async Task CreateSubscriptionAsync_sends_the_customer_id_and_plan_handle_and_maps_the_result()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Subscription());
        var client = MaxioTestContext.CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync(MaxioPayloads.CUSTOMER_ID, MaxioTestContext.PRO_HANDLE);

        Assert.Equal(MaxioPayloads.SUBSCRIPTION_ID, subscription.Id);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(299.00m, subscription.PlanPrice);
        Assert.Equal(MaxioTestContext.PRO_HANDLE, subscription.PlanHandle);
        Assert.Equal(MaxioPayloads.CUSTOMER_REFERENCE, subscription.CustomerReference);
        Assert.True(subscription.IsActive);
        Assert.False(subscription.IsPendingCancellation);
        Assert.Equal(DateTimeOffset.Parse(MaxioPayloads.PERIOD_END), subscription.CurrentPeriodEndsAt);

        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("\"subscription\"", body);
        Assert.Contains("\"customer_id\"", body);
        Assert.Contains("\"product_handle\"", body);
        Assert.Contains(MaxioTestContext.PRO_HANDLE, body);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_surfaces_the_providers_own_validation_message()
    {
        var handler = StubHttpMessageHandler.Failing(HttpStatusCode.UnprocessableEntity,
            MaxioPayloads.ErrorList("Product requires a payment method"));
        var client = MaxioTestContext.CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateSubscriptionAsync(MaxioPayloads.CUSTOMER_ID, MaxioTestContext.PRO_HANDLE));

        Assert.Equal("CreateSubscription", exception.Operation);
        Assert.Contains("Product requires a payment method", exception.ProviderMessage);
    }

    [Fact]
    public async Task GetSubscriptionAsync_returns_null_for_an_unknown_id()
    {
        var handler = StubHttpMessageHandler.Failing(HttpStatusCode.NotFound);
        var client = MaxioTestContext.CreateClient(handler);

        Assert.Null(await client.GetSubscriptionAsync(999999));
    }

    [Fact]
    public async Task GetSubscriptionAsync_reports_a_subscription_that_is_pending_cancellation()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Subscription(
            cancelAtEndOfPeriod: true,
            delayedCancelAt: MaxioPayloads.PERIOD_END));
        var client = MaxioTestContext.CreateClient(handler);

        var subscription = await client.GetSubscriptionAsync(MaxioPayloads.SUBSCRIPTION_ID);

        Assert.True(subscription!.IsPendingCancellation);
        Assert.Equal(DateTimeOffset.Parse(MaxioPayloads.PERIOD_END), subscription.DelayedCancelAt);
    }

    [Fact]
    public async Task GetSubscriptionAsync_reports_a_scheduled_plan_change()
    {
        var handler = StubHttpMessageHandler.Returning(
            MaxioPayloads.Subscription(nextProductHandle: MaxioTestContext.BASIC_HANDLE));
        var client = MaxioTestContext.CreateClient(handler);

        var subscription = await client.GetSubscriptionAsync(MaxioPayloads.SUBSCRIPTION_ID);

        Assert.Equal(MaxioTestContext.BASIC_HANDLE, subscription!.ScheduledPlanHandle);
    }

    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("canceled", SubscriptionStatus.Canceled)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("trialing", SubscriptionStatus.Trialing)]
    [InlineData("expired", SubscriptionStatus.Expired)]
    [InlineData("unpaid", SubscriptionStatus.Unpaid)]
    [InlineData("on_hold", SubscriptionStatus.Paused)]
    [InlineData("paused", SubscriptionStatus.Paused)]
    public async Task GetSubscriptionAsync_maps_each_provider_state_onto_the_domain_state(string wireState, SubscriptionStatus expected)
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Subscription(state: wireState));
        var client = MaxioTestContext.CreateClient(handler);

        var subscription = await client.GetSubscriptionAsync(MaxioPayloads.SUBSCRIPTION_ID);

        Assert.Equal(expected, subscription!.Status);
    }

    [Fact]
    public async Task GetSubscriptionAsync_maps_an_unrecognised_provider_state_to_unknown_rather_than_throwing()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Subscription(state: "some_future_state"));
        var client = MaxioTestContext.CreateClient(handler);

        var subscription = await client.GetSubscriptionAsync(MaxioPayloads.SUBSCRIPTION_ID);

        Assert.Equal(SubscriptionStatus.Unknown, subscription!.Status);
        Assert.False(subscription.IsActive);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_returns_every_subscription_the_customer_holds()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.SubscriptionList(
            MaxioPayloads.SubscriptionBody(),
            MaxioPayloads.SubscriptionBody(state: "canceled", productHandle: MaxioTestContext.BASIC_HANDLE, productPriceInCents: 2900)));
        var client = MaxioTestContext.CreateClient(handler);

        var subscriptions = (await client.ListSubscriptionsForCustomerAsync(MaxioPayloads.CUSTOMER_ID)).ToList();

        Assert.Equal(2, subscriptions.Count);
        Assert.Single(subscriptions, subscription => subscription.IsActive);
        Assert.Contains(subscriptions, subscription => subscription.PlanPrice == 29.00m);
    }

    [Fact]
    public async Task ListSubscriptionsForCustomerAsync_returns_an_empty_list_for_a_customer_with_none()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.EmptySubscriptionList());
        var client = MaxioTestContext.CreateClient(handler);

        Assert.Empty(await client.ListSubscriptionsForCustomerAsync(MaxioPayloads.CUSTOMER_ID));
    }

    // ---------- Usage ----------

    [Fact]
    public async Task RecordUsageAsync_sends_the_quantity_and_memo_and_returns_the_accepted_record()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Usage(quantity: 5));
        var client = MaxioTestContext.CreateClient(handler);

        var record = await client.RecordUsageAsync(MaxioPayloads.SUBSCRIPTION_ID, MaxioPayloads.COMPONENT_ID, 5m, "eShopOnWeb order 1001");

        Assert.Equal(9001, record.Id);
        Assert.Equal(5m, record.Quantity);
        Assert.Equal(MaxioPayloads.COMPONENT_ID, record.ComponentId);
        Assert.Equal("eShopOnWeb order 1001", record.Memo);

        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("\"usage\"", body);
        Assert.Contains("\"quantity\"", body);
        Assert.Contains("eShopOnWeb order 1001", body);
    }

    [Fact]
    public async Task RecordUsageAsync_reads_a_quantity_the_provider_sends_as_a_string()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Usage(quantity: "12"));
        var client = MaxioTestContext.CreateClient(handler);

        var record = await client.RecordUsageAsync(MaxioPayloads.SUBSCRIPTION_ID, MaxioPayloads.COMPONENT_ID, 12m, null);

        Assert.Equal(12m, record.Quantity);
    }

    [Fact]
    public async Task RecordUsageAsync_surfaces_a_provider_rejection_as_a_typed_exception()
    {
        var handler = StubHttpMessageHandler.Failing(HttpStatusCode.UnprocessableEntity,
            MaxioPayloads.ErrorList("Component is not metered"));
        var client = MaxioTestContext.CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.RecordUsageAsync(MaxioPayloads.SUBSCRIPTION_ID, MaxioPayloads.COMPONENT_ID, 1m, null));

        Assert.Equal("CreateUsage", exception.Operation);
        Assert.Contains("Component is not metered", exception.ProviderMessage);
    }

    [Fact]
    public async Task GetComponentUnitBalanceAsync_returns_the_running_balance()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.SubscriptionComponent(unitBalance: 17));
        var client = MaxioTestContext.CreateClient(handler);

        var balance = await client.GetComponentUnitBalanceAsync(MaxioPayloads.SUBSCRIPTION_ID, MaxioPayloads.COMPONENT_ID);

        Assert.Equal(17, balance);
    }

    [Fact]
    public async Task SumUsageSinceAsync_adds_up_every_reported_quantity()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.UsageList(
            MaxioPayloads.UsageBody(1, 3),
            MaxioPayloads.UsageBody(2, "4"),
            MaxioPayloads.UsageBody(3, 5)));
        var client = MaxioTestContext.CreateClient(handler);

        var total = await client.SumUsageSinceAsync(MaxioPayloads.SUBSCRIPTION_ID, MaxioPayloads.COMPONENT_ID, DateTimeOffset.Parse(MaxioPayloads.PERIOD_START));

        Assert.Equal(12m, total);
    }

    [Fact]
    public async Task SumUsageSinceAsync_returns_zero_when_nothing_has_been_reported()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.EmptyUsageList());
        var client = MaxioTestContext.CreateClient(handler);

        Assert.Equal(0m, await client.SumUsageSinceAsync(MaxioPayloads.SUBSCRIPTION_ID, MaxioPayloads.COMPONENT_ID, null));
    }

    // ---------- Plan change ----------

    [Fact]
    public async Task PreviewPlanChangeAsync_converts_every_proration_amount_from_cents()
    {
        // Read subscription, read target product, then the preview itself.
        var handler = StubHttpMessageHandler.Sequence(
            MaxioPayloads.Subscription(),
            MaxioPayloads.BasicProduct(),
            MaxioPayloads.MigrationPreview());
        var client = MaxioTestContext.CreateClient(handler);

        var preview = await client.PreviewPlanChangeAsync(MaxioPayloads.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE);

        Assert.Equal(-150.00m, preview.ProratedAdjustment);
        Assert.Equal(14.50m, preview.ProratedCharge);
        Assert.Equal(-135.50m, preview.AmountDue);
        Assert.Equal(0m, preview.CreditApplied);
        Assert.Equal(MaxioTestContext.BASIC_HANDLE, preview.TargetPlanHandle);
        Assert.Equal("Basic Plan", preview.TargetPlanName);
        Assert.Equal(29.00m, preview.TargetPlanPrice);
        Assert.Equal(299.00m, preview.CurrentPlanPrice);
        Assert.Equal(PlanChangeTiming.Immediately, preview.Timing);
    }

    [Fact]
    public async Task PreviewPlanChangeAsync_reports_a_configuration_error_for_an_unresolvable_target_plan()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
            request.Method == HttpMethod.Get && request.RequestUri!.ToString().Contains("product", StringComparison.OrdinalIgnoreCase)
                ? StubHttpMessageHandler.Json("{}", HttpStatusCode.NotFound)
                : StubHttpMessageHandler.Json(MaxioPayloads.Subscription()));
        var client = MaxioTestContext.CreateClient(handler);

        await Assert.ThrowsAsync<BillingConfigurationException>(
            () => client.PreviewPlanChangeAsync(MaxioPayloads.SUBSCRIPTION_ID, "no-such-plan"));
    }

    [Fact]
    public async Task ChangePlanImmediatelyAsync_sends_the_target_plan_and_returns_the_updated_subscription()
    {
        var handler = StubHttpMessageHandler.Returning(
            MaxioPayloads.Subscription(productHandle: MaxioTestContext.BASIC_HANDLE, productPriceInCents: 2900));
        var client = MaxioTestContext.CreateClient(handler);

        var updated = await client.ChangePlanImmediatelyAsync(MaxioPayloads.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE);

        Assert.Equal(MaxioTestContext.BASIC_HANDLE, updated.PlanHandle);
        Assert.Equal(29.00m, updated.PlanPrice);

        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("\"migration\"", body);
        Assert.Contains(MaxioTestContext.BASIC_HANDLE, body);
    }

    [Fact]
    public async Task ChangePlanAtRenewalAsync_asks_the_provider_to_defer_the_product_change()
    {
        var handler = StubHttpMessageHandler.Returning(
            MaxioPayloads.Subscription(nextProductHandle: MaxioTestContext.BASIC_HANDLE));
        var client = MaxioTestContext.CreateClient(handler);

        var updated = await client.ChangePlanAtRenewalAsync(MaxioPayloads.SUBSCRIPTION_ID, MaxioTestContext.BASIC_HANDLE);

        Assert.Equal(MaxioTestContext.BASIC_HANDLE, updated.ScheduledPlanHandle);

        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("\"subscription\"", body);
        Assert.Contains("\"product_change_delayed\":true", body);
    }

    // ---------- Lifecycle ----------

    [Fact]
    public async Task PauseSubscriptionAsync_returns_the_subscription_in_its_paused_state()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Subscription(state: "on_hold"));
        var client = MaxioTestContext.CreateClient(handler);

        var updated = await client.PauseSubscriptionAsync(MaxioPayloads.SUBSCRIPTION_ID);

        Assert.Equal(SubscriptionStatus.Paused, updated.Status);
        Assert.True(updated.IsPaused);
    }

    [Fact]
    public async Task ResumeSubscriptionAsync_returns_the_subscription_in_its_active_state()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Subscription(state: "active"));
        var client = MaxioTestContext.CreateClient(handler);

        var updated = await client.ResumeSubscriptionAsync(MaxioPayloads.SUBSCRIPTION_ID);

        Assert.Equal(SubscriptionStatus.Active, updated.Status);
    }

    [Fact]
    public async Task CancelSubscriptionImmediatelyAsync_sends_the_reason_and_returns_the_cancelled_subscription()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Subscription(state: "canceled"));
        var client = MaxioTestContext.CreateClient(handler);

        var updated = await client.CancelSubscriptionImmediatelyAsync(MaxioPayloads.SUBSCRIPTION_ID, "Too expensive");

        Assert.Equal(SubscriptionStatus.Canceled, updated.Status);

        var body = Assert.Single(handler.Requests).Body;
        Assert.Contains("Too expensive", body);
    }

    [Fact]
    public async Task CancelSubscriptionImmediatelyAsync_sends_no_scheduling_options_so_the_cancel_takes_effect_at_once()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Subscription(state: "canceled"));
        var client = MaxioTestContext.CreateClient(handler);

        await client.CancelSubscriptionImmediatelyAsync(MaxioPayloads.SUBSCRIPTION_ID, null);

        // Any scheduling field would turn this into a deferred cancellation.
        var body = Assert.Single(handler.Requests).Body ?? string.Empty;
        Assert.DoesNotContain("cancel_at_end_of_period", body);
        Assert.DoesNotContain("scheduled_cancellation_at", body);
        Assert.DoesNotContain("cancellation_message", body);
    }

    [Fact]
    public async Task CancelSubscriptionAtPeriodEndAsync_re_reads_the_subscription_because_the_provider_returns_only_a_message()
    {
        // The delayed-cancel call answers with an acknowledgement, so the client must read the
        // subscription back to report the new flags.
        var handler = StubHttpMessageHandler.Sequence(
            MaxioPayloads.DelayedCancellation(),
            MaxioPayloads.Subscription(cancelAtEndOfPeriod: true, delayedCancelAt: MaxioPayloads.PERIOD_END));
        var client = MaxioTestContext.CreateClient(handler);

        var updated = await client.CancelSubscriptionAtPeriodEndAsync(MaxioPayloads.SUBSCRIPTION_ID, "Switching provider");

        Assert.True(updated.IsPendingCancellation);
        Assert.Equal(DateTimeOffset.Parse(MaxioPayloads.PERIOD_END), updated.DelayedCancelAt);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ReactivateSubscriptionAsync_returns_the_reactivated_subscription()
    {
        var handler = StubHttpMessageHandler.Returning(MaxioPayloads.Subscription(state: "active"));
        var client = MaxioTestContext.CreateClient(handler);

        var updated = await client.ReactivateSubscriptionAsync(MaxioPayloads.SUBSCRIPTION_ID);

        Assert.Equal(SubscriptionStatus.Active, updated.Status);
    }

    [Fact]
    public async Task Lifecycle_calls_surface_a_provider_rejection_as_a_typed_exception()
    {
        var handler = StubHttpMessageHandler.Failing(HttpStatusCode.UnprocessableEntity,
            MaxioPayloads.ErrorList("Cannot pause a canceled subscription"));
        var client = MaxioTestContext.CreateClient(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.PauseSubscriptionAsync(MaxioPayloads.SUBSCRIPTION_ID));

        Assert.Equal("PauseSubscription", exception.Operation);
        Assert.Contains("Cannot pause a canceled subscription", exception.ProviderMessage);
    }

    // ---------- Transport ----------

    [Fact]
    public async Task An_unreachable_provider_is_reported_as_a_billing_provider_failure_with_no_status()
    {
        var client = MaxioTestContext.CreateClient(StubHttpMessageHandler.Unreachable());

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.GetSubscriptionAsync(MaxioPayloads.SUBSCRIPTION_ID));

        Assert.Equal("ReadSubscription", exception.Operation);
        Assert.Null(exception.StatusCode);
        Assert.Contains("could not be reached", exception.ProviderMessage);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }
}
