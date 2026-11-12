using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Rq = MaxioAdvancedBilling.Requests;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public partial class MaxioBillingService
{
    // ---------------------------------------------------------------- F2 subscribe

    public async Task<SubscriptionDto> Subscribe(string user, string? planHandle, CancellationToken ct)
    {
        var plan = await RequireOfferedHandle(planHandle, ct); // before anything is written
        var customerId = await EnsureCustomer(user, ct);
        var reference = $"eshop-{Guid.NewGuid():N}";

        // claim -> SDK call -> release; a shopper may hold several subscriptions, so the claim lives only while in flight
        var created = await Claimed($"sub:{user}:{plan.Handle}", () => Run(t => _client.Subscriptions.CreateSubscription(
            new Rq.Subscriptions.CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerId = customerId,
                        PaymentCollectionMethod = CollectionMethod.Remittance, // no card details
                        Reference = reference,
                    }
                }
            }, cancellationToken: t), ct,
            settle: t => OrNull(() => _client.Subscriptions.FindSubscription(new Rq.Subscriptions.FindSubscriptionRequest { Reference = reference }, cancellationToken: t))));
        return ToDto(created.Subscription, null);
    }

    // ---------------------------------------------------------------- F3 read + paid

    public async Task<SubscriptionDto> GetSubscription(string user, int subscriptionId, CancellationToken ct)
    {
        var sub = await GetOwned(subscriptionId, user, ct);
        return ToDto(sub, await IsPaid(sub, ct));
    }

    private async Task<bool> IsPaid(Subscription sub, CancellationToken ct)
    {
        if ((sub.BalanceInCents ?? 0) != 0) return false;
        var state = sub.State;
        if (state == SubscriptionState.PastDue || state == SubscriptionState.Unpaid || state == SubscriptionState.SoftFailure) return false;

        for (var page = 1; ; page++)
        {
            var p0 = page;
            var r = await Run(t => _client.Invoices.ListInvoices(new Rq.Invoices.ListInvoicesRequest
            {
                SubscriptionId = sub.Id,
                Status = InvoiceStatus.Open,
                Page = p0,
                PerPage = PageSize,
            }, cancellationToken: t), ct);
            foreach (var inv in r.Invoices)
            {
                if (decimal.TryParse(inv.DueAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var due) && due > 0) return false;
            }
            if (r.Invoices.Count < PageSize) return true;
            if (page >= MaxPages) throw new MaxioFailure(503, "Could not determine whether the subscription is paid, try again later.");
        }
    }

    // ---------------------------------------------------------------- F4 / F5

    public async Task<SubscriptionDto> Pause(string user, int id, CancellationToken ct)
    {
        var sub = await GetOwned(id, user, ct);
        if (sub.State != SubscriptionState.Active && sub.State != SubscriptionState.Trialing) throw Conflict("pause", sub);
        if (sub.NextAssessmentAt is { } next && next - DateTimeOffset.UtcNow < TimeSpan.FromHours(24))
            throw new MaxioFailure(409, "A subscription cannot be paused within 24 hours of its next billing date.");

        var r = await Run(t => _client.SubscriptionStatus.PauseSubscription(new Rq.SubscriptionStatus.PauseSubscriptionRequest { SubscriptionId = id }, cancellationToken: t), ct,
            settle: t => Reached(_client, id, s => s.State == SubscriptionState.OnHold || s.State == SubscriptionState.Paused, t));
        return ToDto(r.Subscription, null);
    }

    public async Task<SubscriptionDto> Resume(string user, int id, CancellationToken ct)
    {
        var sub = await GetOwned(id, user, ct);
        if (sub.State != SubscriptionState.OnHold && sub.State != SubscriptionState.Paused) throw Conflict("resume", sub);

        var r = await Run(t => _client.SubscriptionStatus.ResumeSubscription(new Rq.SubscriptionStatus.ResumeSubscriptionRequest { SubscriptionId = id }, cancellationToken: t), ct,
            settle: t => Reached(_client, id, s => s.State != SubscriptionState.OnHold && s.State != SubscriptionState.Paused, t));
        return ToDto(r.Subscription, null);
    }

    public async Task<SubscriptionDto> Cancel(string user, int id, CancellationToken ct)
    {
        var sub = await GetOwned(id, user, ct);
        if (sub.State == SubscriptionState.Canceled || sub.State == SubscriptionState.Expired
            || sub.State == SubscriptionState.Pending || sub.State == SubscriptionState.FailedToCreate) throw Conflict("cancel", sub);

        var r = await Run(t => _client.SubscriptionStatus.CancelSubscription(new Rq.SubscriptionStatus.CancelSubscriptionRequest { SubscriptionId = id }, cancellationToken: t), ct,
            settle: t => Reached(_client, id, s => s.State == SubscriptionState.Canceled, t));
        return ToDto(r.Subscription, null);
    }

    public async Task<SubscriptionDto> Restart(string user, int id, CancellationToken ct)
    {
        var sub = await GetOwned(id, user, ct);
        if (sub.State != SubscriptionState.Canceled) throw Conflict("restart", sub);

        var r = await Claimed($"react:{id}", () => Run(t => _client.SubscriptionStatus.ReactivateSubscription(
            new Rq.SubscriptionStatus.ReactivateSubscriptionOperationRequest { SubscriptionId = id }, cancellationToken: t), ct,
            settle: t => Reached(_client, id, s => s.State != SubscriptionState.Canceled, t)));
        return ToDto(r.Subscription, null);
    }

    // ---------------------------------------------------------------- F6 switch

    public async Task<SubscriptionDto> Switch(string user, int id, string? planHandle, CancellationToken ct)
    {
        var plan = await RequireOfferedHandle(planHandle, ct); // before a cancelled subscription is reactivated (and billed)
        var sub = await GetOwned(id, user, ct);
        var state = sub.State;
        if (state == SubscriptionState.Expired || state == SubscriptionState.FailedToCreate || state == SubscriptionState.Pending) throw Conflict("switch", sub);

        var canceled = state == SubscriptionState.Canceled;
        if (!canceled && string.Equals(sub.Product?.Handle, plan.Handle, StringComparison.Ordinal))
            throw new MaxioFailure(400, "The subscription is already on this plan.");

        if (!canceled) return await ChangeProduct(id, plan, ct);

        return await Claimed($"switch:{id}", async () =>
        {
            var reactivated = await Run(t => _client.SubscriptionStatus.ReactivateSubscription(
                new Rq.SubscriptionStatus.ReactivateSubscriptionOperationRequest { SubscriptionId = id }, cancellationToken: t), ct,
                settle: t => Reached(_client, id, s => s.State != SubscriptionState.Canceled, t));
            if (string.Equals(reactivated.Subscription.Product?.Handle, plan.Handle, StringComparison.Ordinal))
                return ToDto(reactivated.Subscription, null);
            try
            {
                return await ChangeProduct(id, plan, ct);
            }
            catch (MaxioFailure f)
            {
                throw new MaxioFailure(f.Status, f.Message + " The subscription was restarted on its previous plan.", f);
            }
        });
    }

    private async Task<SubscriptionDto> ChangeProduct(int id, PlanDto plan, CancellationToken ct)
    {
        var r = await Run(t => _client.Subscriptions.UpdateSubscription(new Rq.Subscriptions.UpdateSubscriptionOperationRequest
        {
            SubscriptionId = id,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { ProductHandle = plan.Handle } },
        }, cancellationToken: t), ct,
        settle: t => Reached(_client, id, s => s.Product?.Handle == plan.Handle, t));
        if (!string.Equals(r.Subscription.Product?.Handle, plan.Handle, StringComparison.Ordinal))
            throw new MaxioFailure(409, "The billing system did not move the subscription to the new plan immediately.");
        return ToDto(r.Subscription, null);
    }

    // ---------------------------------------------------------------- F7 usage for a day

    public async Task<UsageDayDto> UsageForDay(string user, int id, string? date, CancellationToken ct)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new MaxioFailure(400, "date is required as YYYY-MM-DD.");
        await GetOwned(id, user, ct);

        var comps = await Run(t => _client.SubscriptionComponents.ListSubscriptionComponents(
            new Rq.SubscriptionComponents.ListSubscriptionComponentsRequest { SubscriptionId = id }, cancellationToken: t), ct);
        var metered = comps.Select(c => c.Component).Where(c => c?.ComponentId != null && c.Kind == ComponentKind.MeteredComponent).ToList();

        var since = new DateTimeOffset(day.Date, TimeSpan.Zero);
        var until = since.AddDays(1);
        var result = new List<ComponentUsageDto>();
        var anyTruncated = false;

        foreach (var c in metered)
        {
            var records = new List<UsageRecordDto>();
            var truncated = false;
            for (var page = 1; ; page++)
            {
                var p0 = page;
                var batch = await Run(t => _client.SubscriptionComponents.ListUsages(new Rq.SubscriptionComponents.ListUsagesRequest
                {
                    SubscriptionIdOrReference = id,
                    ComponentId = c!.ComponentId!.Value,
                    SinceDate = since,
                    UntilDate = until,
                    Page = p0,
                    PerPage = PageSize,
                }, cancellationToken: t), ct);
                foreach (var item in batch)
                {
                    var u = item.Usage;
                    // until_date is inclusive: drop anything recorded on the following day
                    if (u.CreatedAt is { } at && at.Date != day.Date) continue;
                    records.Add(new UsageRecordDto(u.Id, QuantityOf(u.Quantity), u.Memo, u.CreatedAt));
                }
                if (batch.Count < PageSize) break;
                if (page >= MaxPages) { truncated = true; break; }
            }
            anyTruncated |= truncated;
            result.Add(new ComponentUsageDto(c!.ComponentId!.Value, c.ComponentHandle, c.Name, c.UnitName,
                records.Sum(r => r.Quantity), records, truncated));
        }
        return new UsageDayDto(id, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result, anyTruncated);
    }

    private static decimal QuantityOf(Quantity1? q)
    {
        if (q == null) return 0;
        if (q.TryGetInt(out var i)) return i;
        if (q.TryGetString(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    // ---------------------------------------------------------------- F10 next billing date

    public async Task<SubscriptionDto> SetNextBillingDate(int id, string? date, CancellationToken ct)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            throw new MaxioFailure(400, "date is required as YYYY-MM-DD.");
        if (day.Date <= DateTime.UtcNow.Date) throw new MaxioFailure(400, "date must be in the future.");

        var read = await Run(t => _client.Subscriptions.ReadSubscription(new Rq.Subscriptions.ReadSubscriptionRequest { SubscriptionId = id }, cancellationToken: t), ct);
        var sub = read.Subscription;
        var st = sub.State;
        if (st == SubscriptionState.Canceled || st == SubscriptionState.Expired || st == SubscriptionState.OnHold || st == SubscriptionState.Paused
            || st == SubscriptionState.Pending || st == SubscriptionState.FailedToCreate) throw Conflict("change the billing date of", sub);

        var target = new DateTimeOffset(day.Year, day.Month, day.Day, 12, 0, 0, TimeSpan.Zero);
        bool Applied(Subscription s) => s.CurrentPeriodEndsAt is { } e && (e.Date == day.Date || e.UtcDateTime.Date == day.Date);

        var r = await Run(t => _client.Subscriptions.UpdateSubscription(new Rq.Subscriptions.UpdateSubscriptionOperationRequest
        {
            SubscriptionId = id,
            Body = new UpdateSubscriptionRequest { Subscription = new UpdateSubscription { NextBillingAt = target } },
        }, cancellationToken: t), ct,
        settle: t => Reached(_client, id, Applied, t));

        // The update response does not echo next_billing_at; the period end is what proves the change landed.
        if (!Applied(r.Subscription))
        {
            var again = await Reached(_client, id, Applied, ct);
            if (again == null) throw new MaxioFailure(502, "The billing system did not apply the new billing date.");
            r = again;
        }
        return ToDto(r.Subscription, null) with { NextBillingAt = r.Subscription.CurrentPeriodEndsAt };
    }
}
