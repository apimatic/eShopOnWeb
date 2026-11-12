using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Invoices;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using MaxioAdvancedBilling.Requests.SubscriptionProducts;
using MaxioAdvancedBilling.Requests.SubscriptionStatus;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MaxioCustomers = MaxioAdvancedBilling.Requests.Customers;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The shopper-facing subscription operations. Maxio is the system of record: a shopper is the Maxio
/// customer whose <c>reference</c> is the shopper's user name, and a subscription belongs to the shopper
/// when its customer carries that reference.
/// </summary>
public class SubscriptionService
{
    private const int PAGE_SIZE = 200;

    private static readonly HashSet<string> EndedStates = new(StringComparer.Ordinal)
    {
        "canceled", "expired", "failed_to_create"
    };

    private static readonly HashSet<string> OwingStates = new(StringComparer.Ordinal)
    {
        "past_due", "unpaid", "soft_failure"
    };

    private readonly MaxioClientProvider _clientProvider;
    private readonly MaxioCall _call;
    private readonly PlanCatalog _plans;
    private readonly KeyedLock _locks;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(MaxioClientProvider clientProvider, MaxioCall call, PlanCatalog plans, KeyedLock locks,
        UserManager<ApplicationUser> userManager, ILogger<SubscriptionService> logger)
    {
        _clientProvider = clientProvider;
        _call = call;
        _plans = plans;
        _locks = locks;
        _userManager = userManager;
        _logger = logger;
    }

    private MaxioAdvancedBillingClient Client => _clientProvider.GetClient();

    // ---- identity -------------------------------------------------------------------------------

    /// <summary>The canonical user name of the signed-in shopper (from the JWT), or a 401.</summary>
    public async Task<string> ResolveShopperAsync(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BillingException(HttpStatusCode.Unauthorized, "The token does not identify a user.");
        }
        var user = await _userManager.FindByNameAsync(name);
        if (user?.UserName == null)
        {
            throw new BillingException(HttpStatusCode.Unauthorized, "The token's user does not exist.");
        }
        return user.UserName;
    }

    // ---- subscribe ------------------------------------------------------------------------------

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string? planHandle, CancellationToken ct)
    {
        var shopper = await ResolveShopperAsync(principal);
        var plan = await _plans.FindOfferedPlanAsync(planHandle, ct);
        var user = (await _userManager.FindByNameAsync(shopper))!;

        // One subscribe at a time per shopper in this process; Maxio remains the durable record.
        using var claim = await _locks.AcquireAsync(shopper, ct);

        var customer = await FindCustomerAsync(shopper, ct);
        if (customer?.Id != null)
        {
            var existing = await ListCustomerSubscriptionsAsync(customer.Id.Value, ct);
            var held = existing.FirstOrDefault(s =>
                !EndedStates.Contains(s.State?.Value ?? string.Empty) &&
                string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));
            if (held != null)
            {
                throw BillingException.Conflict(
                    $"You already hold subscription {held.Id} to plan '{plan.Handle}'. Switch or cancel it instead.");
            }
        }

        var body = new CreateSubscription
        {
            ProductHandle = plan.Handle,
            // No card details: the shopper pays by remittance (invoice).
            PaymentCollectionMethod = CollectionMethod.Remittance,
        };
        if (customer?.Id != null)
        {
            body = body with { CustomerId = customer.Id };
        }
        else
        {
            body = body with
            {
                CustomerAttributes = new CustomerAttributes
                {
                    Reference = shopper,
                    Email = user.Email ?? shopper,
                    FirstName = shopper,
                    LastName = "Shopper",
                }
            };
        }

        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var created = await _call.RunAsync(() => Client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest { Body = new CreateSubscriptionRequest { Subscription = body } },
                cancellationToken: ct));
            return await ToDtoAsync(created.Subscription, ct);
        }
        catch (BillingException ex) when (ex.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
        {
            // The connection failed or timed out: Maxio may still have created the subscription.
            // Settle the outcome by looking for it before reporting anything.
            var settled = await FindJustCreatedAsync(shopper, plan.Handle!, startedAt, ct);
            if (settled == null) throw;
            _logger.LogWarning("Subscribe to {Plan} failed on the wire but subscription {Id} exists; returning it", plan.Handle, settled.Id);
            return await ToDtoAsync(settled, ct);
        }
    }

    private async Task<Subscription?> FindJustCreatedAsync(string shopper, string planHandle, DateTimeOffset since, CancellationToken ct)
    {
        try
        {
            var customer = await FindCustomerAsync(shopper, ct);
            if (customer?.Id == null) return null;
            return (await ListCustomerSubscriptionsAsync(customer.Id.Value, ct))
                .Where(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                            s.CreatedAt != null && s.CreatedAt >= since.AddSeconds(-5) &&
                            !EndedStates.Contains(s.State?.Value ?? string.Empty))
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();
        }
        catch (BillingException)
        {
            return null;
        }
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken ct)
    {
        try
        {
            var found = await _call.RunAsync(() => Client.Customers.ReadCustomerByReference(
                new MaxioCustomers.ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct));
            return string.Equals(found.Customer.Reference, reference, StringComparison.OrdinalIgnoreCase) ? found.Customer : null;
        }
        catch (BillingException ex) when (MaxioCall.IsNotFound(ex))
        {
            return null;
        }
    }

    private async Task<List<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var all = new List<Subscription>();
        for (var page = 1; ; page++)
        {
            var batch = await _call.RunAsync(() => Client.Subscriptions.ListSubscriptions(
                new ListSubscriptionsRequest { CustomerId = customerId, Page = page, PerPage = PAGE_SIZE }, cancellationToken: ct));
            all.AddRange(batch.Select(b => b.Subscription));
            if (batch.Count < PAGE_SIZE) break;
        }
        return all;
    }

    // ---- read -----------------------------------------------------------------------------------

    /// <summary>
    /// Reads a subscription and checks it belongs to the shopper. Another shopper's subscription is
    /// reported as not found, so ids cannot be probed.
    /// </summary>
    public async Task<Subscription> GetOwnedAsync(ClaimsPrincipal principal, int subscriptionId, CancellationToken ct)
    {
        var shopper = await ResolveShopperAsync(principal);
        var subscription = await ReadSubscriptionAsync(subscriptionId, ct);
        if (!string.Equals(subscription.Customer?.Reference, shopper, StringComparison.OrdinalIgnoreCase))
        {
            throw BillingException.NotFound($"There is no subscription {subscriptionId} for you.");
        }
        return subscription;
    }

    public async Task<Subscription> ReadSubscriptionAsync(int subscriptionId, CancellationToken ct)
    {
        var response = await _call.RunAsync(() => Client.Subscriptions.ReadSubscription(
            new ReadSubscriptionRequest { SubscriptionId = subscriptionId }, cancellationToken: ct));
        return response.Subscription;
    }

    public async Task<SubscriptionDto> GetAsync(ClaimsPrincipal principal, int subscriptionId, CancellationToken ct) =>
        await ToDtoAsync(await GetOwnedAsync(principal, subscriptionId, ct), ct);

    public async Task<SubscriptionDto> ToDtoAsync(Subscription subscription, CancellationToken ct)
    {
        var due = await AmountDueAsync(subscription, ct);
        return new SubscriptionDto
        {
            Id = subscription.Id ?? 0,
            State = subscription.State?.Value,
            Paid = due <= 0m,
            AmountDue = due < 0m ? 0m : due,
            Plan = new PlanSummaryDto
            {
                Id = subscription.Product?.Id,
                Handle = subscription.Product?.Handle,
                Name = subscription.Product?.Name,
            },
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt,
        };
    }

    /// <summary>
    /// What the shopper still owes: the larger of the subscription's outstanding balance and the sum of
    /// its open invoices (a site on relationship invoicing keeps the debt on invoices). A subscription in
    /// a dunning state owes money even if neither figure says so yet.
    /// </summary>
    private async Task<decimal> AmountDueAsync(Subscription subscription, CancellationToken ct)
    {
        var balance = (subscription.BalanceInCents ?? 0L) / 100m;
        var invoiced = 0m;
        if (subscription.Id != null)
        {
            for (var page = 1; ; page++)
            {
                var batch = await _call.RunAsync(() => Client.Invoices.ListInvoices(
                    new ListInvoicesRequest { SubscriptionId = subscription.Id, Status = InvoiceStatus.Open, Page = page, PerPage = PAGE_SIZE },
                    cancellationToken: ct));
                foreach (var invoice in batch.Invoices)
                {
                    if (decimal.TryParse(invoice.DueAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                    {
                        invoiced += amount;
                    }
                }
                if (batch.Invoices.Count < PAGE_SIZE) break;
            }
        }
        var due = Math.Max(balance, invoiced);
        if (due <= 0m && OwingStates.Contains(subscription.State?.Value ?? string.Empty))
        {
            // Dunning state with no figure: still owing.
            due = 0.01m;
        }
        return due;
    }

    // ---- status changes -------------------------------------------------------------------------

    public async Task<SubscriptionDto> PauseAsync(ClaimsPrincipal principal, int id, CancellationToken ct)
    {
        var current = await GetOwnedAsync(principal, id, ct);
        var state = current.State?.Value;
        if (state == "on_hold" || state == "paused") throw BillingException.Conflict("The subscription is already paused.");
        if (EndedStates.Contains(state ?? string.Empty)) throw BillingException.Conflict($"A {state} subscription cannot be paused.");

        var response = await _call.RunAsync(() => Client.SubscriptionStatus.PauseSubscription(
            new PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct));
        return await ToDtoAsync(response.Subscription, ct);
    }

    public async Task<SubscriptionDto> ResumeAsync(ClaimsPrincipal principal, int id, CancellationToken ct)
    {
        var current = await GetOwnedAsync(principal, id, ct);
        var state = current.State?.Value;
        if (state != "on_hold" && state != "paused") throw BillingException.Conflict("The subscription is not paused.");

        var response = await _call.RunAsync(() => Client.SubscriptionStatus.ResumeSubscription(
            new ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct));
        return await ToDtoAsync(response.Subscription, ct);
    }

    public async Task<SubscriptionDto> CancelAsync(ClaimsPrincipal principal, int id, CancellationToken ct)
    {
        var current = await GetOwnedAsync(principal, id, ct);
        var state = current.State?.Value;
        if (EndedStates.Contains(state ?? string.Empty)) throw BillingException.Conflict($"The subscription is already {state}.");

        var response = await _call.RunAsync(() => Client.SubscriptionStatus.CancelSubscription(
            new CancelSubscriptionRequest { SubscriptionId = id }, cancellationToken: ct));
        return await ToDtoAsync(response.Subscription, ct);
    }

    public async Task<SubscriptionDto> RestartAsync(ClaimsPrincipal principal, int id, CancellationToken ct)
    {
        var current = await GetOwnedAsync(principal, id, ct);
        if (current.State?.Value != "canceled") throw BillingException.Conflict("Only a cancelled subscription can be restarted.");

        var response = await _call.RunAsync(() => Client.SubscriptionStatus.ReactivateSubscription(
            new ReactivateSubscriptionOperationRequest { SubscriptionId = id }, cancellationToken: ct));
        return await ToDtoAsync(response.Subscription, ct);
    }

    // ---- switch plan ----------------------------------------------------------------------------

    public async Task<SubscriptionDto> SwitchPlanAsync(ClaimsPrincipal principal, int id, string? planHandle, CancellationToken ct)
    {
        var current = await GetOwnedAsync(principal, id, ct);
        var plan = await _plans.FindOfferedPlanAsync(planHandle, ct);
        if (string.Equals(current.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
        {
            throw BillingException.Conflict($"The subscription is already on plan '{plan.Handle}'.");
        }

        if (EndedStates.Contains(current.State?.Value ?? string.Empty))
        {
            // Maxio refuses a product migration on a cancelled subscription (422) but accepts the
            // product being set on it, which puts the subscription on the new plan.
            await _call.RunAsync(() => Client.Subscriptions.UpdateSubscription(
                new UpdateSubscriptionOperationRequest
                {
                    SubscriptionId = id,
                    Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = plan.Handle } }
                }, cancellationToken: ct));
        }
        else
        {
            await _call.RunAsync(() => Client.SubscriptionProducts.MigrateSubscriptionProduct(
                new MigrateSubscriptionProductRequest
                {
                    SubscriptionId = id,
                    Body = new SubscriptionProductMigrationRequest
                    {
                        Migration = new SubscriptionProductMigration { ProductHandle = plan.Handle }
                    }
                }, cancellationToken: ct));
        }

        // Confirm the subscription really is on the new plan.
        var after = await ReadSubscriptionAsync(id, ct);
        if (!string.Equals(after.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Subscription {Id} is on {Actual} after a switch to {Wanted}", id, after.Product?.Handle, plan.Handle);
            throw new BillingException(HttpStatusCode.BadGateway, "The billing system did not move the subscription to the new plan.");
        }
        return await ToDtoAsync(after, ct);
    }

    // ---- usage ----------------------------------------------------------------------------------

    public async Task<SubscriptionUsageDto> GetUsageAsync(ClaimsPrincipal principal, int id, string? dateText, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            throw BillingException.BadRequest("The 'date' query parameter is required and must look like YYYY-MM-DD.");
        }
        await GetOwnedAsync(principal, id, ct);

        var components = await _call.RunAsync(() => Client.SubscriptionComponents.ListSubscriptionComponents(
            new ListSubscriptionComponentsRequest { SubscriptionId = id }, cancellationToken: ct));
        var metered = components
            .Select(c => c.Component)
            .Where(c => c?.ComponentId != null && c.Kind == ComponentKind.MeteredComponent)
            .GroupBy(c => c!.ComponentId!.Value)
            .Select(g => g.First()!)
            .ToList();

        // Maxio compares since/until with created_at at site-local midnight, and until_date is inclusive of
        // that midnight. Ask for a day on each side and keep what falls on the day in created_at's own offset.
        var since = new DateTimeOffset(day.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var until = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var result = new SubscriptionUsageDto { SubscriptionId = id, Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        foreach (var component in metered)
        {
            var componentId = component.ComponentId!.Value;
            var usage = new ComponentUsageDto { ComponentId = componentId, Name = component.Name, UnitName = component.UnitName };
            for (var page = 1; ; page++)
            {
                var batch = await _call.RunAsync(() => Client.SubscriptionComponents.ListUsages(
                    new ListUsagesRequest
                    {
                        SubscriptionIdOrReference = id,
                        ComponentId = componentId,
                        SinceDate = since,
                        UntilDate = until,
                        Page = page,
                        PerPage = PAGE_SIZE,
                    }, cancellationToken: ct));
                foreach (var item in batch)
                {
                    var created = item.Usage.CreatedAt;
                    if (created == null || DateOnly.FromDateTime(created.Value.DateTime) != day) continue;
                    usage.Records.Add(new UsageRecordDto
                    {
                        Id = item.Usage.Id,
                        CreatedAt = created,
                        Quantity = ReadQuantity(item.Usage.Quantity),
                        Memo = item.Usage.Memo,
                    });
                }
                if (batch.Count < PAGE_SIZE) break;
            }
            usage.Quantity = usage.Records.Sum(r => r.Quantity);
            result.Components.Add(usage);
        }
        return result;
    }

    private static decimal ReadQuantity(Quantity1? quantity)
    {
        if (quantity == null) return 0m;
        if (quantity.TryGetInt(out var asInt)) return asInt;
        if (quantity.TryGetString(out var asString) &&
            decimal.TryParse(asString, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        if (quantity.TryGetUnknown(out var element) && element.ValueKind == JsonValueKind.Number &&
            element.TryGetDecimal(out var number)) return number;
        return 0m;
    }

    // ---- staff: next billing date ---------------------------------------------------------------

    public async Task<SubscriptionDto> SetNextBillingDateAsync(int id, DateOnly date, CancellationToken ct)
    {
        var current = await ReadSubscriptionAsync(id, ct);
        var state = current.State?.Value ?? string.Empty;
        if (state is "canceled" or "expired" or "failed_to_create" or "on_hold" or "paused" or "pending" or "awaiting_signup")
        {
            throw BillingException.Conflict($"A {state} subscription is not billed, so it has no next billing date to set.");
        }

        // Dates are site-local: take the site's offset from what Maxio returned for this subscription.
        var offset = current.CurrentPeriodEndsAt?.Offset ?? current.CreatedAt?.Offset ?? TimeSpan.Zero;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(offset).DateTime);
        if (date <= today)
        {
            throw BillingException.BadRequest($"The next billing date must be after today ({today:yyyy-MM-dd}).");
        }

        // Noon local keeps the calendar date stable whatever the site's time zone.
        var at = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), offset);
        var updated = await _call.RunAsync(() => Client.Subscriptions.UpdateSubscription(
            new UpdateSubscriptionOperationRequest
            {
                SubscriptionId = id,
                Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = at } }
            }, cancellationToken: ct));

        // The response does not carry next_billing_at; current_period_ends_at shows it took effect.
        var after = await ReadSubscriptionAsync(id, ct);
        var applied = after.CurrentPeriodEndsAt ?? updated.Subscription.CurrentPeriodEndsAt;
        if (applied == null || DateOnly.FromDateTime(applied.Value.DateTime) != date)
        {
            _logger.LogError("Subscription {Id}: next billing date {Wanted} not applied (period ends {Actual})", id, date, applied);
            throw new BillingException(HttpStatusCode.BadGateway, "The billing system did not apply the next billing date.");
        }
        return await ToDtoAsync(after, ct);
    }
}
