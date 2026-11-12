using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.Extensions.Logging;
using SdkSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>What a shopper (or staff member) sees of a subscription.</summary>
public record SubscriptionView(
    int Id,
    string State,
    bool Paid,
    long BalanceInCents,
    decimal Balance,
    int? PlanId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CanceledAt,
    DateTimeOffset? CreatedAt);

public record UsageRecordView(long? Id, decimal Quantity, string? Memo, DateTimeOffset? RecordedAt);

public record ComponentUsageView(int? ComponentId, string? ComponentHandle, string? Name, string? UnitName, decimal TotalQuantity, IReadOnlyList<UsageRecordView> Records);

public record DailyUsageView(int SubscriptionId, string Date, IReadOnlyList<ComponentUsageView> Components);

/// <summary>
/// Subscription use cases on top of Maxio (the system of record). A shopper is identified by the JWT user name, which
/// is the <c>reference</c> of their Maxio customer; a subscription belongs to the shopper whose customer owns it.
/// </summary>
public class SubscriptionBilling
{
    private const int PageSize = 200;
    private const int MaxPages = 50;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly PlanCatalog _plans;
    private readonly MaxioCustomers _customers;
    private readonly ILogger<SubscriptionBilling> _logger;

    public SubscriptionBilling(MaxioAdvancedBillingClient client, PlanCatalog plans, MaxioCustomers customers, ILogger<SubscriptionBilling> logger)
    {
        _client = client;
        _plans = plans;
        _customers = customers;
        _logger = logger;
    }

    // ---- 2. subscribe ------------------------------------------------------------------------------------------

    public async Task<SubscriptionView> SubscribeAsync(string userName, string planHandle, CancellationToken ct)
    {
        var plan = await _plans.FindByHandleAsync(planHandle, ct)
            ?? throw MaxioProblemException.NotFound($"There is no plan with handle '{planHandle.Trim()}' to subscribe to.");
        var customerId = await _customers.EnsureCustomerIdAsync(userName, ct);

        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        try
        {
            var created = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductId = plan.Id,
                        CustomerId = customerId,
                        // No card details: the shopper is invoiced instead of charged automatically.
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                    }
                }
            }, cancellationToken: ct);
            return ToView(created.Subscription ?? throw Unexpected("create the subscription"));
        }
        catch (Exception ex) when (ex is SdkConnectionException)
        {
            // The connection failed (or timed out) after Maxio may have acted: look for the subscription we asked for.
            var settled = await FindCreatedSubscriptionAsync(customerId, plan.Id, startedAt, ct);
            if (settled is not null)
            {
                _logger.LogWarning("Subscription {Id} was created although the create call failed with {Failure}", settled.Id, ex.GetType().Name);
                return ToView(settled);
            }
            throw MaxioErrors.Map(ex, "create the subscription", writeMayHaveLanded: true);
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw MaxioErrors.Map(ex, "create the subscription");
        }
    }

    private async Task<SdkSubscription?> FindCreatedSubscriptionAsync(int customerId, int planId, DateTimeOffset since, CancellationToken ct)
    {
        try
        {
            var list = await _client.Subscriptions.ListSubscriptions(
                new ListSubscriptionsRequest { CustomerId = customerId, PerPage = PageSize }, cancellationToken: ct);
            return list.Select(s => s.Subscription)
                .Where(s => s is not null && s.Product?.Id == planId && s.CreatedAt >= since)
                .OrderByDescending(s => s!.CreatedAt)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is SdkException)
        {
            _logger.LogWarning(ex, "Could not re-read the subscriptions of customer {CustomerId} to settle an unknown create", customerId);
            return null;
        }
    }

    // ---- 3. read -----------------------------------------------------------------------------------------------

    public async Task<SubscriptionView> GetAsync(string userName, int subscriptionId, CancellationToken ct)
        => ToView(await LoadOwnedAsync(userName, subscriptionId, ct));

    // ---- 4. pause / resume -------------------------------------------------------------------------------------

    public async Task<SubscriptionView> PauseAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var current = await LoadOwnedAsync(userName, subscriptionId, ct);
        var state = current.State;
        if (state == SubscriptionState.OnHold) throw MaxioProblemException.Conflict("The subscription is already paused.");
        if (!(state == SubscriptionState.Active || state == SubscriptionState.Trialing || state == SubscriptionState.PastDue))
            throw MaxioProblemException.Conflict($"A subscription that is {Describe(state)} cannot be paused.");

        return await WriteAsync("pause the subscription", subscriptionId, async () =>
            (await _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct)).Subscription, ct);
    }

    public async Task<SubscriptionView> ResumeAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var current = await LoadOwnedAsync(userName, subscriptionId, ct);
        if (current.State != SubscriptionState.OnHold)
            throw MaxioProblemException.Conflict($"Only a paused subscription can be resumed; this one is {Describe(current.State)}.");

        return await WriteAsync("resume the subscription", subscriptionId, async () =>
            (await _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct)).Subscription, ct);
    }

    // ---- 5. cancel / restart -----------------------------------------------------------------------------------

    public async Task<SubscriptionView> CancelAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var current = await LoadOwnedAsync(userName, subscriptionId, ct);
        if (current.State == SubscriptionState.Canceled) throw MaxioProblemException.Conflict("The subscription is already cancelled.");
        if (IsEndOfLife(current.State)) throw MaxioProblemException.Conflict($"A subscription that is {Describe(current.State)} cannot be cancelled.");

        return await WriteAsync("cancel the subscription", subscriptionId, async () =>
            (await _client.SubscriptionStatus.CancelSubscription(new CancelSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct)).Subscription, ct);
    }

    public async Task<SubscriptionView> RestartAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var current = await LoadOwnedAsync(userName, subscriptionId, ct);
        if (current.State != SubscriptionState.Canceled)
            throw MaxioProblemException.Conflict($"Only a cancelled subscription can be restarted; this one is {Describe(current.State)}.");

        return await WriteAsync("restart the subscription", subscriptionId, () => ReactivateAsync(subscriptionId, ct), ct);
    }

    private async Task<SdkSubscription?> ReactivateAsync(int subscriptionId, CancellationToken ct)
        => (await _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest
        {
            SubscriptionId = subscriptionId,
            // Keep the original billing period when it is still running instead of starting (and charging) a new one.
            Body = new ReactivateSubscriptionRequest { Resume = MaxioAdvancedBilling.Models.AnyOf.Resume.Bool(true) }
        }, cancellationToken: ct)).Subscription;

    // ---- 6. switch plan ----------------------------------------------------------------------------------------

    public async Task<SubscriptionView> SwitchPlanAsync(string userName, int subscriptionId, string planHandle, CancellationToken ct)
    {
        var current = await LoadOwnedAsync(userName, subscriptionId, ct);
        var target = await _plans.FindByHandleAsync(planHandle, ct)
            ?? throw MaxioProblemException.NotFound($"There is no plan with handle '{planHandle.Trim()}' to switch to.");
        if (current.Product?.Id == target.Id) throw MaxioProblemException.Conflict("The subscription is already on that plan.");

        SdkSubscription? switched;
        if (current.State == SubscriptionState.Active || current.State == SubscriptionState.Trialing)
        {
            switched = await MigrateAsync(subscriptionId, target, ct);
        }
        else if (current.State == SubscriptionState.Canceled)
        {
            // Maxio only migrates live subscriptions. For a cancelled one: point it at the new plan, then bring it back,
            // so the shopper is billed for the new plan only.
            await WriteAsync("change the plan of the cancelled subscription", subscriptionId, async () =>
                (await _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductId = target.Id } }
                }, cancellationToken: ct)).Subscription, ct);
            var restarted = await WriteAsync("restart the subscription on the new plan", subscriptionId, () => ReactivateAsync(subscriptionId, ct), ct);
            if (restarted.PlanId == target.Id) return restarted;
            if (restarted.State != "active" && restarted.State != "trialing")
                throw new MaxioProblemException(HttpStatusCode.BadGateway, $"The subscription came back {restarted.State} on the old plan and could not be moved to the new one.");
            switched = await MigrateAsync(subscriptionId, target, ct);
        }
        else
        {
            throw MaxioProblemException.Conflict($"A subscription that is {Describe(current.State)} cannot be switched to another plan.");
        }

        var view = ToView(switched ?? throw Unexpected("switch the plan"));
        if (view.PlanId != target.Id)
            throw new MaxioProblemException(HttpStatusCode.BadGateway, "Maxio did not move the subscription to the requested plan.");
        return view;
    }

    private async Task<SdkSubscription?> MigrateAsync(int subscriptionId, OfferedPlan target, CancellationToken ct)
    {
        try
        {
            return await Migrate(preservePeriod: true);
        }
        catch (ApiException<MigrateSubscriptionProductError> ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity
            && MaxioErrors.Describe(ex).Contains("prorated", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return await Migrate(preservePeriod: false);
            }
            catch (Exception inner) when (inner is SdkException)
            {
                throw MaxioErrors.Map(inner, "switch the plan", writeMayHaveLanded: true);
            }
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw MaxioErrors.Map(ex, "switch the plan", writeMayHaveLanded: true);
        }

        async Task<SdkSubscription?> Migrate(bool preservePeriod) =>
            (await _client.SubscriptionProducts.MigrateSubscriptionProduct(new MigrateSubscriptionProductRequest
            {
                SubscriptionId = subscriptionId,
                Body = new SubscriptionProductMigrationRequest
                {
                    Migration = new SubscriptionProductMigration { ProductId = target.Id, PreservePeriod = preservePeriod }
                }
            }, cancellationToken: ct)).Subscription;
    }

    // ---- 7. usage for a day ------------------------------------------------------------------------------------

    public async Task<DailyUsageView> GetUsageAsync(string userName, int subscriptionId, DateOnly day, CancellationToken ct)
    {
        await LoadOwnedAsync(userName, subscriptionId, ct);

        IReadOnlyList<SubscriptionComponentResponse> components;
        try
        {
            components = await _client.SubscriptionComponents.ListSubscriptionComponents(
                new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw MaxioErrors.Map(ex, "list the subscription's components");
        }

        var metered = components.Select(c => c.Component)
            .Where(c => c is { ComponentId: not null } && (c.Kind == ComponentKind.MeteredComponent || c.Kind == ComponentKind.PrepaidUsageComponent))
            .GroupBy(c => c!.ComponentId!.Value)
            .Select(g => g.First()!)
            .ToList();

        var result = new List<ComponentUsageView>();
        foreach (var component in metered)
        {
            var records = await ReadDayAsync(subscriptionId, component.ComponentId!.Value, day, ct);
            result.Add(new ComponentUsageView(component.ComponentId, component.ComponentHandle, component.Name, component.UnitName, records.Sum(r => r.Quantity), records));
        }
        return new DailyUsageView(subscriptionId, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result);
    }

    private async Task<List<UsageRecordView>> ReadDayAsync(int subscriptionId, int componentId, DateOnly day, CancellationToken ct)
    {
        // Maxio's until_date is "less than or equal to midnight" of that date, so ask for [day, day+1] and keep only the
        // records created on the day itself (in the site's own offset, as Maxio reports created_at).
        var since = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = since.AddDays(1);
        var records = new List<UsageRecordView>();
        for (var page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<UsageResponse> batch;
            try
            {
                batch = await _client.SubscriptionComponents.ListUsages(new ListUsagesRequest
                {
                    SubscriptionIdOrReference = subscriptionId,
                    ComponentId = componentId,
                    SinceDate = since,
                    UntilDate = until,
                    Page = page,
                    PerPage = PageSize,
                }, cancellationToken: ct);
            }
            catch (Exception ex) when (ex is SdkException)
            {
                throw MaxioErrors.Map(ex, "read the usage");
            }

            foreach (var usage in batch.Select(b => b.Usage))
            {
                if (usage.CreatedAt is { } created && DateOnly.FromDateTime(created.DateTime) != day) continue;
                records.Add(new UsageRecordView(usage.Id, ParseQuantity(usage.Quantity), usage.Memo, usage.CreatedAt));
            }
            if (batch.Count < PageSize) return records;
        }
        throw new MaxioIncompleteResultException("usage list");
    }

    private static decimal ParseQuantity(MaxioAdvancedBilling.Models.AnyOf.Quantity1? quantity)
    {
        if (quantity is null) return 0;
        if (quantity.TryGetInt(out var whole)) return whole;
        if (quantity.TryGetString(out var text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return 0;
    }

    // ---- 10. next billing date (staff) -------------------------------------------------------------------------

    public async Task<SubscriptionView> SetNextBillingDateAsync(int subscriptionId, DateOnly date, CancellationToken ct)
    {
        var current = await LoadAsync(subscriptionId, ct);
        if (!(current.State == SubscriptionState.Active || current.State == SubscriptionState.Trialing))
            throw MaxioProblemException.Conflict($"The next billing date of a subscription that is {Describe(current.State)} cannot be set.");
        if (date <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw MaxioProblemException.BadRequest("The next billing date must be in the future.");

        // Keep the time of day and the site's UTC offset of the current billing moment, so the date is the site's date.
        var anchor = current.CurrentPeriodEndsAt ?? current.NextAssessmentAt;
        var nextBillingAt = anchor is { } a
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.FromDateTime(a.DateTime)), a.Offset)
            : new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

        await WriteAsync("set the next billing date", subscriptionId, async () =>
            (await _client.Subscriptions.UpdateSubscription(new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = subscriptionId,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = nextBillingAt } }
            }, cancellationToken: ct)).Subscription, ct);

        // Maxio reports the new date as current_period_ends_at; make sure it is the one that was asked for.
        var reread = await LoadAsync(subscriptionId, ct);
        var effective = reread.NextAssessmentAt ?? reread.CurrentPeriodEndsAt;
        if (effective is { } e && DateOnly.FromDateTime(e.DateTime) != date)
            throw new MaxioProblemException(HttpStatusCode.BadGateway, $"Maxio did not apply the next billing date (it reports {e:yyyy-MM-dd}).");
        return ToView(reread);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private async Task<SdkSubscription> LoadAsync(int subscriptionId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct);
            return response.Subscription ?? throw Unexpected("read the subscription");
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw MaxioProblemException.NotFound($"Subscription {subscriptionId} does not exist.");
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw MaxioErrors.Map(ex, "read the subscription");
        }
    }

    /// <summary>Reads the subscription and makes sure the caller's customer owns it; otherwise it simply "does not exist" for them.</summary>
    private async Task<SdkSubscription> LoadOwnedAsync(string userName, int subscriptionId, CancellationToken ct)
    {
        var subscription = await LoadAsync(subscriptionId, ct);
        if (!string.Equals(subscription.Customer?.Reference, userName, StringComparison.Ordinal))
            throw MaxioProblemException.NotFound($"Subscription {subscriptionId} does not exist.");
        return subscription;
    }

    private async Task<SubscriptionView> WriteAsync(string action, int subscriptionId, Func<Task<SdkSubscription?>> write, CancellationToken ct)
    {
        try
        {
            var result = await write();
            return ToView(result ?? throw Unexpected(action));
        }
        catch (Exception ex) when (ex is SdkConnectionException)
        {
            // Outcome unknown: the write may have landed. Report what Maxio holds now rather than a failure.
            _logger.LogWarning("{Action} on subscription {Id} failed with {Failure}; re-reading it", action, subscriptionId, ex.GetType().Name);
            try
            {
                return ToView(await LoadAsync(subscriptionId, ct));
            }
            catch (MaxioProblemException)
            {
                throw MaxioErrors.Map(ex, action, writeMayHaveLanded: true);
            }
        }
        catch (Exception ex) when (ex is SdkException)
        {
            throw MaxioErrors.Map(ex, action);
        }
    }

    private static MaxioProblemException Unexpected(string action)
        => new(HttpStatusCode.BadGateway, $"Maxio answered without a subscription when asked to {action}.");

    private static bool IsEndOfLife(SubscriptionState? state)
        => state == SubscriptionState.Expired || state == SubscriptionState.FailedToCreate || state == SubscriptionState.TrialEnded;

    private static string Describe(SubscriptionState? state) => state is null ? "in an unknown state" : $"'{Wire(state)}'";

    private static string Wire(SubscriptionState state) => state.Value;

    internal static SubscriptionView ToView(SdkSubscription s)
    {
        if (s.Id is null) throw Unexpected("read the subscription");
        // Maxio's balance is what is still owed on the subscription: nothing owed (or a credit) means paid.
        var balance = s.BalanceInCents ?? throw new MaxioProblemException(HttpStatusCode.BadGateway, "Maxio did not report the subscription balance.");
        return new SubscriptionView(
            s.Id.Value,
            s.State is null ? "unknown" : Wire(s.State),
            balance <= 0,
            balance,
            balance / 100m,
            s.Product?.Id,
            s.Product?.Handle,
            s.Product?.Name,
            s.ProductPriceInCents ?? s.Product?.PriceInCents,
            s.Currency,
            s.CurrentPeriodEndsAt,
            s.NextAssessmentAt ?? s.CurrentPeriodEndsAt,
            s.CanceledAt,
            s.CreatedAt);
    }
}
