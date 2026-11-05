using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MaxioSubscription = MaxioAdvancedBilling.Models.Subscription;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscribe / list flows backed by Maxio. Every Maxio write follows: claim (app store, primary key)
/// -> SDK call -> record result. An ambiguous write is reconciled by re-reading Maxio in the write's own catch.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(1);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioPlanCatalog _catalog;
    private readonly MaxioSettings _settings;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioPlanCatalog catalog,
        IOptions<MaxioSettings> settings,
        CatalogContext db,
        UserManager<ApplicationUser> users,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _catalog = catalog;
        _settings = settings.Value;
        _db = db;
        _users = users;
        _logger = logger;
    }

    public Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken) =>
        Bounded(ct => _catalog.ListPlansAsync(ct), cancellationToken);

    public Task<SubscriptionSummary> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken) =>
        Bounded(ct => SubscribeCoreAsync(userName, planHandle, ct), cancellationToken);

    public Task<IReadOnlyList<SubscriptionSummary>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken) =>
        Bounded(ct => ListMineCoreAsync(userName, ct), cancellationToken);

    // ---- subscribe ---------------------------------------------------------------------------------------------

    private async Task<SubscriptionSummary> SubscribeCoreAsync(string userName, string? planHandle, CancellationToken ct)
    {
        var userKey = UserKey(userName);
        planHandle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle?.Trim() : planHandle.Trim();
        if (string.IsNullOrEmpty(planHandle))
        {
            throw new SubscriptionException(400, "A plan handle is required.");
        }

        // Invariant: only a plan this application offers (the family listing) may be subscribed to.
        var offered = await _catalog.ListPlansAsync(ct);
        if (!offered.Plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.Ordinal)))
        {
            throw offered.Truncated
                ? new SubscriptionException(503, "The plan list could not be fully read; try again later.")
                : new SubscriptionException(400, $"\"{planHandle}\" is not an available subscription plan.");
        }

        var customerId = await EnsureCustomerAsync(userKey, userName, ct);
        return await EnsureSubscriptionAsync(userKey, customerId, planHandle, retryStale: true, ct);
    }

    private async Task<int> EnsureCustomerAsync(string userKey, string userName, CancellationToken ct)
    {
        var retriedStale = false;
        while (true)
        {
            var claim = await _db.MaxioCustomerClaims.FindAsync(new object[] { userKey }, ct);
            if (claim is not null)
            {
                if (claim.Status == ClaimStatus.Completed && claim.MaxioCustomerId is int done)
                {
                    return done;
                }

                // Pending: another request is creating it right now, or an earlier one died mid-write.
                if (await ReadCustomerIdAsync(userKey, ct) is int existing)
                {
                    await CompleteCustomerClaimAsync(claim, existing);
                    return existing;
                }
                if (!retriedStale && DateTimeOffset.UtcNow - claim.CreatedAt > StaleClaimAge)
                {
                    retriedStale = true;
                    await ReleaseAsync(claim);
                    continue;
                }
                throw new DuplicateException("A subscription request for this account is already being processed.");
            }

            claim = new MaxioCustomerClaim { UserKey = userKey };
            if (!await TryInsertClaimAsync(claim, ct))
            {
                continue; // lost the race: the loop reads the winner's claim
            }

            return await CreateCustomerClaimedAsync(claim, userName, ct);
        }
    }

    /// <summary>Claim is held. Reuse a customer Maxio already has for this reference, otherwise create one.</summary>
    private async Task<int> CreateCustomerClaimedAsync(MaxioCustomerClaim claim, string userName, CancellationToken ct)
    {
        try
        {
            if (await ReadCustomerIdAsync(claim.UserKey, ct) is int already)
            {
                await CompleteCustomerClaimAsync(claim, already);
                return already;
            }

            var (email, firstName, lastName) = await ResolveIdentityAsync(userName);
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerOperationRequest
                {
                    Body = new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = claim.UserKey
                        }
                    }
                },
                cancellationToken: ct);

            var id = response.Customer?.Id ?? throw NoId(typeof(CustomerResponse));
            await CompleteCustomerClaimAsync(claim, id);
            _logger.LogInformation("Maxio customer {CustomerId} ensured", id);
            return id;
        }
        catch (Exception ex) when (ex is not SubscriptionException && (MaxioErrors.IsUnknownOutcome(ex) || ex is OperationCanceledException))
        {
            // The write may have landed: settle it by re-reading Maxio by the reference we sent.
            _logger.LogWarning(ex, "Maxio customer create had an unknown outcome; reconciling by reference");
            int? landed;
            try
            {
                landed = await ReadCustomerIdAsync(claim.UserKey, CancellationToken.None);
            }
            catch (SubscriptionException)
            {
                // Still unknown: the claim stays Pending and the next request reconciles before creating.
                throw MaxioErrors.Translate(ex, "create the billing customer");
            }

            if (landed is int found)
            {
                await CompleteCustomerClaimAsync(claim, found);
                return found;
            }
            await ReleaseAsync(claim); // provably not created: free the claim so a retry can proceed
            throw new SubscriptionException(503, "Billing could not create the customer; please retry.", ex);
        }
        catch (Exception ex) when (ex is SdkException or SubscriptionException)
        {
            await ReleaseAsync(claim); // Maxio (or we) refused the call: nothing was created
            string? detail = ex is ApiException<CreateCustomerError> typed && typed.Error.TryGetCustomerErrorResponse1(out _)
                ? "the customer details were not accepted"
                : null;
            throw MaxioErrors.Translate(ex, "create the billing customer", detail);
        }
    }

    private async Task<SubscriptionSummary> EnsureSubscriptionAsync(string userKey, int customerId, string planHandle, bool retryStale, CancellationToken ct)
    {
        while (true)
        {
            var claim = await _db.MaxioSubscriptionClaims.FindAsync(new object[] { userKey, planHandle }, ct);
            if (claim is null)
            {
                claim = new MaxioSubscriptionClaim { UserKey = userKey, PlanHandle = planHandle };
                if (!await TryInsertClaimAsync(claim, ct))
                {
                    continue;
                }
                return await CreateSubscriptionClaimedAsync(claim, customerId, ct);
            }

            var live = await FindLiveSubscriptionAsync(customerId, planHandle, claim.MaxioSubscriptionId, ct);
            if (live is not null)
            {
                if (claim.Status != ClaimStatus.Completed)
                {
                    await CompleteSubscriptionClaimAsync(claim, live.Id);
                }
                return Summarize(live);
            }

            if (claim.Status == ClaimStatus.Pending && retryStale && DateTimeOffset.UtcNow - claim.CreatedAt > StaleClaimAge)
            {
                retryStale = false;
                await ReleaseAsync(claim);
                continue;
            }

            throw claim.Status == ClaimStatus.Pending
                ? new DuplicateException("A subscription request for this plan is already being processed.")
                : new SubscriptionException(409, "The previously recorded subscription is no longer active in billing.");
        }
    }

    /// <summary>Claim is held. Reuse a live Maxio subscription for this plan if one exists, otherwise create it.</summary>
    private async Task<SubscriptionSummary> CreateSubscriptionClaimedAsync(MaxioSubscriptionClaim claim, int customerId, CancellationToken ct)
    {
        try
        {
            if (await FindLiveSubscriptionAsync(customerId, claim.PlanHandle, null, ct) is { } existing)
            {
                await CompleteSubscriptionClaimAsync(claim, existing.Id);
                return Summarize(existing);
            }

            var response = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = claim.PlanHandle,
                            CustomerId = customerId,
                            PaymentCollectionMethod = CollectionMethod.TryGetKnownValue(_settings.PaymentCollectionMethod, out var method) ? method : null,
                            Reference = SubscriptionReference(claim)
                        }
                    }
                },
                cancellationToken: ct);

            var created = response.Subscription;
            if (created?.Id is null)
            {
                throw NoId(typeof(SubscriptionResponse));
            }

            await CompleteSubscriptionClaimAsync(claim, created.Id);
            _logger.LogInformation("Maxio subscription {SubscriptionId} created, state {State}", created.Id, created.State?.Value);
            return Summarize(created);
        }
        catch (Exception ex) when (ex is not SubscriptionException && (MaxioErrors.IsUnknownOutcome(ex) || ex is OperationCanceledException))
        {
            // The write may have landed: settle it by listing the customer's subscriptions for this plan.
            _logger.LogWarning(ex, "Maxio subscription create had an unknown outcome; reconciling");
            MaxioSubscription? landed;
            try
            {
                landed = await FindLiveSubscriptionAsync(customerId, claim.PlanHandle, null, CancellationToken.None);
            }
            catch (SubscriptionException)
            {
                // Still unknown: the claim stays Pending and the next request reconciles first.
                throw MaxioErrors.Translate(ex, "create the subscription");
            }

            if (landed is not null)
            {
                await CompleteSubscriptionClaimAsync(claim, landed.Id);
                return Summarize(landed);
            }
            await ReleaseAsync(claim);
            throw new SubscriptionException(503, "Billing could not create the subscription; please retry.", ex);
        }
        catch (Exception ex) when (ex is SdkException or SubscriptionException)
        {
            await ReleaseAsync(claim); // Maxio (or we) refused the call: nothing was created
            string? detail = ex is ApiException<CreateSubscriptionError> typed && typed.Error.TryGetErrorListResponse1(out var list)
                ? MaxioErrors.JoinErrors(list.Errors)
                : null;
            _logger.LogWarning("Maxio subscription create refused ({Status})", (ex as ApiException)?.StatusCode);
            throw MaxioErrors.Translate(ex, "create the subscription", detail);
        }
    }

    // ---- my subscriptions --------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<SubscriptionSummary>> ListMineCoreAsync(string userName, CancellationToken ct)
    {
        var claim = await _db.MaxioCustomerClaims.FindAsync(new object[] { UserKey(userName) }, ct);
        if (claim?.MaxioCustomerId is not int customerId)
        {
            return Array.Empty<SubscriptionSummary>();
        }

        try
        {
            var all = await ListSubscriptionsAsync(customerId, ct);
            return all
                .Where(s => s.Product?.ProductFamily?.Handle is not { } family
                            || string.Equals(family, _settings.ProductFamilyHandle, StringComparison.Ordinal))
                .Select(Summarize)
                .ToList();
        }
        catch (SdkException ex)
        {
            throw MaxioErrors.Translate(ex, "read your subscriptions");
        }
    }

    // ---- Maxio reads -------------------------------------------------------------------------------------------

    /// <summary>The customer id Maxio holds for this reference, or null when Maxio has none (404).</summary>
    private async Task<int?> ReadCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: ct);
            return response.Customer?.Id;
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw MaxioErrors.Translate(ex, "look up the billing customer");
        }
    }

    private async Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var response = await _client.Customers.ListCustomerSubscriptions(
            new ListCustomerSubscriptionsRequest { CustomerId = customerId }, cancellationToken: ct);
        return response.Select(r => r.Subscription).OfType<MaxioSubscription>().ToList();
    }

    private async Task<MaxioSubscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, int? knownId, CancellationToken ct)
    {
        try
        {
            var subs = await ListSubscriptionsAsync(customerId, ct);
            return subs.FirstOrDefault(s => knownId is not null && s.Id == knownId)
                   ?? subs.FirstOrDefault(s => string.Equals(s.Product?.Handle, planHandle, StringComparison.Ordinal) && IsLive(s));
        }
        catch (SdkException ex)
        {
            throw MaxioErrors.Translate(ex, "read the customer's subscriptions");
        }
    }

    private static bool IsLive(MaxioSubscription s) =>
        s.State is null || !(s.State.Is("canceled") || s.State.Is("expired") || s.State.Is("failed_to_create"));

    // ---- claims ------------------------------------------------------------------------------------------------

    /// <summary>Inserts the claim; the primary key rejects a second claimant. False means somebody else holds it.</summary>
    private async Task<bool> TryInsertClaimAsync(object claim, CancellationToken ct)
    {
        _db.Add(claim);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task CompleteCustomerClaimAsync(MaxioCustomerClaim claim, int customerId)
    {
        claim.Status = ClaimStatus.Completed;
        claim.MaxioCustomerId = customerId;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CompleteSubscriptionClaimAsync(MaxioSubscriptionClaim claim, int? subscriptionId)
    {
        claim.Status = ClaimStatus.Completed;
        claim.MaxioSubscriptionId = subscriptionId;
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task ReleaseAsync(object claim)
    {
        _db.Remove(claim);
        await _db.SaveChangesAsync(CancellationToken.None);
        _db.ChangeTracker.Clear();
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private static string UserKey(string userName)
    {
        var key = userName?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key))
        {
            throw new SubscriptionException(401, "The caller could not be identified.");
        }
        return key;
    }

    private static string SubscriptionReference(MaxioSubscriptionClaim claim) => $"eshop:{claim.UserKey}:{claim.PlanHandle}";

    private static ResponseDeserializationException NoId(Type target) =>
        new($"Maxio returned a {target.Name} without an id.", new InvalidOperationException("missing id"))
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri("about:blank"),
            StatusCode = HttpStatusCode.OK,
            Headers = new HttpResponseMessage().Headers,
            ContentType = null,
            TargetType = target
        };

    private async Task<(string Email, string FirstName, string LastName)> ResolveIdentityAsync(string userName)
    {
        var user = await _users.FindByNameAsync(userName);
        var email = !string.IsNullOrWhiteSpace(user?.Email) ? user!.Email! : userName.Contains('@') ? userName : null;
        if (email is null)
        {
            throw new SubscriptionException(422, "The account has no email address on file, which billing requires.");
        }
        var local = email.Split('@')[0];
        return (email, string.IsNullOrWhiteSpace(local) ? "Shopper" : local, "Shopper");
    }

    private static SubscriptionSummary Summarize(MaxioSubscription s) => new(
        s.Id,
        s.State?.Value,
        s.Product?.Handle,
        s.Product?.Name,
        s.ProductPriceInCents ?? s.Product?.PriceInCents,
        s.Product?.Interval,
        s.Product?.IntervalUnit?.Value,
        s.NextAssessmentAt);

    /// <summary>One total budget for everything a request does against Maxio.</summary>
    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken requestAborted)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        cts.CancelAfter(_settings.RequestBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested)
        {
            throw new SubscriptionException(504, "Billing did not respond within the time budget; please retry.");
        }
    }
}
