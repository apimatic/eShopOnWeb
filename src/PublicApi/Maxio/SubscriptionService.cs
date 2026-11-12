using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Shopper subscriptions. Maxio is the system of record: nothing about a subscription is stored locally.
/// A shopper is a Maxio customer whose reference is the user name from the JWT.
/// </summary>
public class SubscriptionService
{
    private const int PAGE_SIZE = 200;
    private const int MAX_PAGES = 100;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioErrors _errors;
    private readonly PlanService _plans;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioAdvancedBillingClient client, MaxioErrors errors, PlanService plans, ILogger<SubscriptionService> logger)
    {
        _client = client;
        _errors = errors;
        _plans = plans;
        _logger = logger;
    }

    /// <summary>The Maxio customer reference of a shopper (user names are case-insensitive).</summary>
    public static string CustomerReference(string userName) => userName.Trim().ToLowerInvariant();

    // ---------------------------------------------------------------- subscribe

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string? planHandle, CancellationToken cancellationToken = default)
    {
        // The plan must be one the shop offers, not merely one that exists in Maxio.
        var plan = await _plans.GetOfferedAsync(planHandle, cancellationToken);
        var reference = CustomerReference(userName);
        await EnsureCustomerAsync(userName, reference, cancellationToken);

        // The subscription's own reference is this write's key: it settles an unknown outcome.
        var subscriptionReference = $"eshop-{Guid.NewGuid():N}";
        var request = new CreateSubscriptionOperationRequest
        {
            Body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = plan.Handle,
                    CustomerReference = reference,
                    Reference = subscriptionReference,
                    // No card details: the shopper is invoiced and pays against the invoice.
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            }
        };

        try
        {
            var response = await _errors.RunAsync("CreateSubscription", () => _client.Subscriptions.CreateSubscription(request, cancellationToken: cancellationToken));
            return ToDto(response.Subscription ?? throw new BillingException(502, "The billing system did not return the new subscription."));
        }
        catch (BillingException ex) when (ex.InnerException is not null && MaxioErrors.IsUnknownOutcome(ex.InnerException))
        {
            var found = await FindByReferenceAsync(subscriptionReference, cancellationToken);
            if (found is not null)
            {
                return ToDto(found);
            }

            throw new BillingException(504, "The billing system did not confirm the subscription. It was not created; try again.", ex);
        }
    }

    private async Task<Subscription?> FindByReferenceAsync(string subscriptionReference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = subscriptionReference }, cancellationToken: cancellationToken);
            return response.Subscription;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>One Maxio customer per shopper, however many subscriptions they hold.</summary>
    private async Task EnsureCustomerAsync(string userName, string reference, CancellationToken cancellationToken)
    {
        if (await CustomerExistsAsync(reference, cancellationToken))
        {
            return;
        }

        var name = userName.Trim();
        var local = name.Contains('@') ? name[..name.IndexOf('@')] : name;
        var email = name.Contains('@') ? name : $"{local}@users.invalid";
        try
        {
            await _errors.RunAsync("CreateCustomer", () => _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = string.IsNullOrWhiteSpace(local) ? "Shopper" : local,
                            LastName = "Shopper",
                            Email = email,
                            Reference = reference
                        }
                    }
                },
                cancellationToken: cancellationToken));
        }
        catch (BillingException)
        {
            // Maxio refuses a duplicate reference, so the loser of two simultaneous first subscriptions lands here.
            if (await CustomerExistsAsync(reference, cancellationToken))
            {
                return;
            }

            throw;
        }
    }

    private async Task<bool> CustomerExistsAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex) when (MaxioErrors.IsStatus(ex, HttpStatusCode.NotFound))
        {
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw _errors.Translate("ReadCustomerByReference", ex);
        }
    }

    // ---------------------------------------------------------------- read

    /// <summary>The subscription, provided it belongs to the shopper. Anyone else's is reported as not found.</summary>
    public async Task<Subscription> LoadOwnedAsync(string userName, int subscriptionId, CancellationToken cancellationToken = default)
    {
        var subscription = await ReadAsync(subscriptionId, cancellationToken);
        if (!string.Equals(subscription.Customer?.Reference, CustomerReference(userName), StringComparison.OrdinalIgnoreCase))
        {
            throw new BillingException(404, $"Subscription {subscriptionId} was not found.");
        }

        return subscription;
    }

    public async Task<Subscription> ReadAsync(int subscriptionId, CancellationToken cancellationToken = default)
    {
        var response = await _errors.RunAsync("ReadSubscription",
            () => _client.Subscriptions.ReadSubscription(new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: cancellationToken));
        return response.Subscription ?? throw new BillingException(404, $"Subscription {subscriptionId} was not found.");
    }

    public async Task<SubscriptionDto> GetAsync(string userName, int subscriptionId, CancellationToken cancellationToken = default) =>
        ToDto(await LoadOwnedAsync(userName, subscriptionId, cancellationToken));

    public static SubscriptionDto ToDto(Subscription s) => new()
    {
        Id = s.Id ?? 0,
        State = s.State?.Value ?? string.Empty,
        BalanceInCents = s.BalanceInCents ?? 0,
        // Owes nothing: no outstanding balance on the subscription.
        Paid = (s.BalanceInCents ?? 0) <= 0,
        PlanId = s.Product?.Id,
        PlanHandle = s.Product?.Handle,
        PlanName = s.Product?.Name,
        NextBillingDate = s.NextAssessmentAt,
        CanceledAt = s.CanceledAt,
        CreatedAt = s.CreatedAt
    };

    // ---------------------------------------------------------------- state changes

    public Task<SubscriptionDto> PauseAsync(string userName, int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(userName, subscriptionId, "PauseSubscription",
            check: s =>
            {
                if (Is(s, SubscriptionState.OnHold))
                {
                    return "The subscription is already paused.";
                }

                return IsEnded(s) ? $"A {s.State?.Value} subscription cannot be paused." : null;
            },
            call: () => _client.SubscriptionStatus.PauseSubscription(new PauseSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: cancellationToken),
            achieved: s => Is(s, SubscriptionState.OnHold),
            cancellationToken);

    public Task<SubscriptionDto> ResumeAsync(string userName, int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(userName, subscriptionId, "ResumeSubscription",
            check: s => Is(s, SubscriptionState.OnHold) ? null : "Only a paused subscription can be resumed.",
            call: () => _client.SubscriptionStatus.ResumeSubscription(new ResumeSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: cancellationToken),
            achieved: s => !Is(s, SubscriptionState.OnHold),
            cancellationToken);

    public Task<SubscriptionDto> CancelAsync(string userName, int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(userName, subscriptionId, "CancelSubscription",
            check: s => IsEnded(s) ? "The subscription is already canceled." : null,
            call: () => _client.SubscriptionStatus.CancelSubscription(
                new CancelSubscriptionRequest { SubscriptionId = subscriptionId, Body = new CancellationRequest { Subscription = new CancellationOptions() } },
                cancellationToken: cancellationToken),
            achieved: s => Is(s, SubscriptionState.Canceled),
            cancellationToken);

    public Task<SubscriptionDto> RestartAsync(string userName, int subscriptionId, CancellationToken cancellationToken = default) =>
        TransitionAsync(userName, subscriptionId, "ReactivateSubscription",
            check: s => IsEnded(s) ? null : "Only a canceled subscription can be restarted.",
            call: () => _client.SubscriptionStatus.ReactivateSubscription(new ReactivateSubscriptionOperationRequest { SubscriptionId = subscriptionId }, cancellationToken: cancellationToken),
            achieved: s => !IsEnded(s),
            cancellationToken);

    /// <summary>
    /// Switches the subscription to another offered plan, in any state. A product change through UpdateSubscription
    /// works on a canceled subscription too, where a migration would be refused.
    /// </summary>
    public async Task<SubscriptionDto> SwitchPlanAsync(string userName, int subscriptionId, string? planHandle, CancellationToken cancellationToken = default)
    {
        var plan = await _plans.GetOfferedAsync(planHandle, cancellationToken);
        return await TransitionAsync(userName, subscriptionId, "SwitchSubscriptionPlan",
            check: s => string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase)
                ? $"The subscription is already on plan '{plan.Handle}'."
                : null,
            call: () => _client.Subscriptions.UpdateSubscription(
                new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = plan.Handle } }
                },
                cancellationToken: cancellationToken),
            achieved: s => string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase),
            cancellationToken);
    }

    /// <summary>Staff: sets the date the subscriber is next billed.</summary>
    public async Task<SubscriptionDto> SetNextBillingDateAsync(int subscriptionId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var current = await ReadAsync(subscriptionId, cancellationToken);
        if (IsEnded(current) || current.NextAssessmentAt is null)
        {
            throw new BillingException(409, $"A {current.State?.Value} subscription has no upcoming billing date to change.");
        }

        // Keep the subscription's own time of day, in the site's offset, so the date means the same day for the subscriber.
        var existing = current.NextAssessmentAt.Value;
        var target = new DateTimeOffset(date.ToDateTime(TimeOnly.FromTimeSpan(existing.TimeOfDay)), existing.Offset);
        if (target <= DateTimeOffset.UtcNow)
        {
            throw new BillingException(400, "The next billing date must be in the future.");
        }

        bool Applied(Subscription s) => s.NextAssessmentAt is { } next && DateOnly.FromDateTime(next.Date) == date;

        try
        {
            await _errors.RunAsync("UpdateSubscription(next_billing_at)", () => _client.Subscriptions.UpdateSubscription(
                new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = subscriptionId,
                    Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } }
                },
                cancellationToken: cancellationToken));
        }
        catch (BillingException ex) when (ex.InnerException is not null && MaxioErrors.IsUnknownOutcome(ex.InnerException))
        {
            // fall through: the re-read below settles whether the date was applied
            _logger.LogWarning("Next billing date update for subscription {SubscriptionId} had an unknown outcome; re-reading", subscriptionId);
        }

        // Maxio's update response does not echo the new date, so read it back and check it.
        var after = await ReadAsync(subscriptionId, cancellationToken);
        if (!Applied(after))
        {
            throw new BillingException(502, "The billing system did not apply the new billing date.");
        }

        return ToDto(after);
    }

    private async Task<SubscriptionDto> TransitionAsync<TResponse>(
        string userName,
        int subscriptionId,
        string operation,
        Func<Subscription, string?> check,
        Func<Task<TResponse>> call,
        Func<Subscription, bool> achieved,
        CancellationToken cancellationToken)
    {
        var current = await LoadOwnedAsync(userName, subscriptionId, cancellationToken);
        var refusal = check(current);
        if (refusal is not null)
        {
            throw new BillingException(409, refusal);
        }

        try
        {
            var response = await _errors.RunAsync(operation, call);
            var subscription = (response as SubscriptionResponse)?.Subscription;
            return ToDto(subscription ?? await ReadAsync(subscriptionId, cancellationToken));
        }
        catch (BillingException ex) when (ex.InnerException is not null && MaxioErrors.IsUnknownOutcome(ex.InnerException))
        {
            // The connection failed after Maxio may have acted: settle it by reading the subscription back.
            var after = await ReadAsync(subscriptionId, cancellationToken);
            if (achieved(after))
            {
                return ToDto(after);
            }

            throw new BillingException(504, "The billing system did not confirm the change. It was not applied; try again.", ex);
        }
    }

    private static bool Is(Subscription s, SubscriptionState state) => s.State == state;

    private static bool IsEnded(Subscription s) =>
        s.State == SubscriptionState.Canceled || s.State == SubscriptionState.Expired || s.State == SubscriptionState.TrialEnded
        || s.State == SubscriptionState.FailedToCreate;

    // ---------------------------------------------------------------- usage

    /// <summary>The metered usage recorded on one day, per metered component of the subscription.</summary>
    public async Task<UsageDto> GetUsageAsync(string userName, int subscriptionId, DateOnly day, CancellationToken cancellationToken = default)
    {
        await LoadOwnedAsync(userName, subscriptionId, cancellationToken);

        var components = await _errors.RunAsync("ListSubscriptionComponents",
            () => _client.SubscriptionComponents.ListSubscriptionComponents(new ListSubscriptionComponentsRequest { SubscriptionId = subscriptionId }, cancellationToken: cancellationToken));

        var result = new UsageDto { SubscriptionId = subscriptionId, Date = day.ToString("yyyy-MM-dd") };
        foreach (var component in components.Select(c => c.Component).Where(c => c?.ComponentId is not null && c.Kind == ComponentKind.MeteredComponent))
        {
            var entries = await ListDayUsagesAsync(subscriptionId, component!.ComponentId!.Value, day, cancellationToken);
            result.Components.Add(new ComponentUsageDto
            {
                ComponentId = component.ComponentId,
                ComponentHandle = component.ComponentHandle,
                Name = component.Name,
                UnitName = component.UnitName,
                TotalQuantity = entries.Sum(e => e.Quantity),
                Usages = entries
            });
        }

        result.TotalQuantity = result.Components.Sum(c => c.TotalQuantity);
        return result;
    }

    private Task<List<UsageEntryDto>> ListDayUsagesAsync(int subscriptionId, int componentId, DateOnly day, CancellationToken cancellationToken) =>
        _errors.RunAsync("ListUsages", async () =>
        {
            // Maxio bounds usages by created_at at midnight in the site's time zone. Ask for a day either side,
            // then keep the usages whose own local date (Maxio returns site-local timestamps) is the requested day.
            var since = new DateTimeOffset(day.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var until = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var entries = new List<UsageEntryDto>();
            for (var page = 1; page <= MAX_PAGES; page++)
            {
                var usages = await _client.SubscriptionComponents.ListUsages(
                    new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = SubscriptionIdOrReference.Int(subscriptionId),
                        ComponentId = ComponentIdModel.Int(componentId),
                        SinceDate = since,
                        UntilDate = until,
                        Page = page,
                        PerPage = PAGE_SIZE
                    },
                    cancellationToken: cancellationToken);

                entries.AddRange(usages
                    .Select(u => u.Usage)
                    .Where(u => u.CreatedAt is { } created && DateOnly.FromDateTime(created.Date) == day)
                    .Select(u => new UsageEntryDto { Id = u.Id, Quantity = QuantityOf(u.Quantity), Memo = u.Memo, RecordedAt = u.CreatedAt }));

                if (usages.Count < PAGE_SIZE)
                {
                    return entries;
                }
            }

            throw new BillingException(502, "The billing system returned more usage records than can be listed.");
        });

    private static decimal QuantityOf(Quantity1? quantity)
    {
        if (quantity is null)
        {
            return 0;
        }

        if (quantity.TryGetInt(out var whole))
        {
            return whole;
        }

        return quantity.TryGetString(out var text) && decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }
}
