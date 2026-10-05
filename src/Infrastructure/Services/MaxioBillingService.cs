using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.MaxioBilling;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Maxio Advanced Billing integration. Every SDK call is guarded by the same failure ladder
/// (typed provider errors → unknown outcomes → raw/transport failures), writes are made idempotent
/// with read-before-write + a local claim, and connection failures on a write are reconciled
/// against the provider inside the failing call's own catch.
/// </summary>
public class MaxioBillingService : IMaxioBillingService
{
    // The total budget one caller request may spend across the SDK calls it triggers. The SDK's
    // Retry.Timeout (30s) is per attempt, so this deadline is the only whole-call bound.
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(60);
    private const int PlanPageCap = 10;
    private const int PlanPageSize = 200;

    private const string StatePending = "pending";
    private static readonly string[] EndOfLifeStates =
    {
        "canceled", "expired", "failed_to_create", "trial_ended", "on_hold", "unpaid", "suspended", "paused"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IRepository<MaxioCustomerLink> _customerLinks;
    private readonly IRepository<MaxioSubscriptionLink> _subscriptionLinks;
    private readonly string _productFamilyHandle;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IRepository<MaxioCustomerLink> customerLinks,
        IRepository<MaxioSubscriptionLink> subscriptionLinks,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _customerLinks = customerLinks;
        _subscriptionLinks = subscriptionLinks;
        _productFamilyHandle = options.Value.ProductFamilyHandle
            ?? throw new InvalidOperationException($"{MaxioOptions.SectionName}:ProductFamilyHandle is not configured.");
        _logger = logger;
    }

    public async Task<PlanCatalog> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var plans = new List<SubscriptionPlanInfo>();
        var truncated = false;

        await BoundedAsync(async token =>
        {
            for (var page = 1; page <= PlanPageCap; page++)
            {
                IReadOnlyList<ProductResponse> products;
                try
                {
                    products = await _client.ProductFamilies.ListProductsForProductFamily(
                        new ListProductsForProductFamilyRequest
                        {
                            ProductFamilyId = $"handle:{_productFamilyHandle}",
                            Page = page,
                            PerPage = PlanPageSize
                        },
                        cancellationToken: token);
                }
                catch (ApiException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out _))
                        throw new MaxioBillingException(
                            $"The configured product family '{_productFamilyHandle}' was not found in the billing system.",
                            ex.StatusCode, ex);
                    if (ex.Error.TryGetRawError(out var raw))
                        throw RawError(raw, ex.StatusCode, ex);
                    throw new MaxioBillingException("The billing system returned an unrecognized error.", ex.StatusCode, ex);
                }
                catch (Exception ex) when (IsProviderFailure(ex))
                {
                    throw Translate(ex);
                }

                plans.AddRange(products.Select(product => MapPlan(product.Product)));

                if (products.Count < PlanPageSize)
                    return; // provider signalled the last page

                if (page == PlanPageCap)
                {
                    truncated = true; // page cap hit before a short page — the list may be incomplete
                    _logger.LogWarning(
                        "Plan catalogue for family {FamilyHandle} hit the {Pages}-page cap; the result is truncated.",
                        _productFamilyHandle, PlanPageCap);
                }
            }
        }, cancellationToken);

        return new PlanCatalog(plans, truncated);
    }

    public async Task<SubscriptionInfo> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
            throw new MaxioBillingException("A productHandle is required.", HttpStatusCode.BadRequest);

        var catalog = await GetPlansAsync(cancellationToken);
        var plan = catalog.Plans.FirstOrDefault(p => p.Handle == productHandle)
            ?? throw new MaxioBillingException($"Unknown subscription plan '{productHandle}'.", HttpStatusCode.NotFound);

        await EnsureCustomerAsync(shopper, cancellationToken);

        var links = await _subscriptionLinks.ListAsync(new MaxioSubscriptionLinksForShopperSpec(shopper.UserId, productHandle), cancellationToken);

        // 1. Settle any claim left pending by an earlier call whose outcome was unknown.
        var pending = links.FirstOrDefault(link => link.MaxioSubscriptionId == 0);
        if (pending is not null)
        {
            var settled = await FindSubscriptionOrNullAsync(pending.MaxioReference, cancellationToken);
            if (settled is null)
            {
                // The claim never reached the provider — release it and reuse its reference.
                await ReleaseClaimAsync(pending);
                links = links.Where(link => link != pending).ToList();
            }
            else
            {
                await RecordLinkAsync(pending, settled);
                if (!IsEndOfLife(settled.State?.Value))
                    return MapSubscription(settled, productHandle);
                links = links.Where(link => link != pending).Append(pending).ToList();
            }
        }

        // 2. A live subscription on this plan is returned as-is (idempotent re-subscribe).
        var liveLink = links.FirstOrDefault(link => link.MaxioSubscriptionId != 0 && !IsEndOfLife(link.State));
        if (liveLink is not null)
        {
            var existing = await FindSubscriptionOrNullAsync(liveLink.MaxioReference, cancellationToken);
            if (existing is not null && !IsEndOfLife(existing.State?.Value))
                return MapSubscription(existing, productHandle);
        }

        // 3. Build the next reference for this shopper + plan and claim it. The primary-key insert
        //    is what rejects a concurrent double-click before it reaches the provider.
        var committed = links.Count(link => link.MaxioSubscriptionId != 0);
        var reference = committed == 0
            ? $"{shopper.UserId}:{productHandle}"
            : $"{shopper.UserId}:{productHandle}-{committed + 1}";

        var claim = new MaxioSubscriptionLink
        {
            MaxioReference = reference,
            UserId = shopper.UserId,
            ProductHandle = productHandle,
            State = StatePending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        try
        {
            await _subscriptionLinks.AddAsync(claim, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost the race — settle from the provider instead of creating a duplicate.
            var inFlight = await FindSubscriptionOrNullAsync(reference, cancellationToken);
            if (inFlight is not null && !IsEndOfLife(inFlight.State?.Value))
                return MapSubscription(inFlight, productHandle);

            throw new MaxioBillingException(
                "A subscription request for this plan is already in progress; try again shortly.",
                HttpStatusCode.Conflict);
        }

        // 4. Create the subscription at the provider, carrying the claimed reference so the
        //    outcome can be settled by lookup.
        Subscription? created = null;
        try
        {
            await BoundedAsync(async token =>
            {
                var response = await _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionOperationRequest
                    {
                        Body = new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = productHandle,
                                CustomerReference = shopper.UserId,
                                Reference = reference,
                                // This integration captures no payment method and the offered plans
                                // require none, so the subscription is billed for remittance instead
                                // of an automatic card charge at signup.
                                PaymentCollectionMethod = CollectionMethod.Remittance
                            }
                        }
                    },
                    cancellationToken: token);
                created = response.Subscription;
            }, cancellationToken);

            _logger.LogInformation(
                "Subscription {Reference} created for shopper {UserId} on plan {ProductHandle}.",
                reference, shopper.UserId, productHandle);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            var message = ex.Error.TryGetErrorListResponse1(out var list)
                ? $"The billing system rejected the subscription: {string.Join("; ", list.Errors)}"
                : ex.Error.TryGetRawError(out var raw)
                    ? RawMessage(raw, ex.StatusCode)
                    : "The billing system returned an unrecognized error.";
            await ReleaseClaimAsync(claim);
            throw new MaxioBillingException(message, ex.StatusCode, ex);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            return await SettleUnknownCreateOutcomeAsync(claim, reference, productHandle, ex, cancellationToken);
        }
        catch (Exception ex) when (ex is not MaxioBillingException and not OperationCanceledException)
        {
            await ReleaseClaimAsync(claim);
            throw Translate(ex);
        }

        if (created is null)
        {
            // A 2xx body that does not name a subscription — the outcome is unknown, not failed.
            return await SettleUnknownCreateOutcomeAsync(
                claim, reference, productHandle,
                new MaxioBillingException("The billing system returned an empty subscription body.", HttpStatusCode.BadGateway),
                cancellationToken);
        }

        await RecordLinkAsync(claim, created);
        return MapSubscription(created, productHandle);
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsForShopperAsync(ShopperIdentity shopper, CancellationToken cancellationToken = default)
    {
        var customer = await ReadCustomerByReferenceOrNullAsync(shopper.UserId, cancellationToken);
        if (customer?.Id is null or 0)
            return Array.Empty<SubscriptionInfo>();

        IReadOnlyList<SubscriptionResponse> subscriptions = Array.Empty<SubscriptionResponse>();
        try
        {
            await BoundedAsync(async token =>
            {
                subscriptions = await _client.Customers.ListCustomerSubscriptions(
                    new ListCustomerSubscriptionsRequest { CustomerId = customer.Id.Value },
                    cancellationToken: token);
            }, cancellationToken);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            throw Translate(ex);
        }

        var result = new List<SubscriptionInfo>();
        foreach (var response in subscriptions)
        {
            if (response.Subscription is null)
            {
                _logger.LogWarning(
                    "ListCustomerSubscriptions for shopper {UserId} returned an entry without a subscription body; skipped.",
                    shopper.UserId);
                continue;
            }
            result.Add(MapSubscription(response.Subscription, response.Subscription.Product?.Handle ?? string.Empty));
        }
        return result;
    }

    // --- customer ---------------------------------------------------------------

    /// <summary>
    /// Idempotent: reads the Maxio customer by reference (== eShop user id) and creates it only
    /// when absent. The local primary-key claim closes the concurrent-creation race.
    /// </summary>
    private async Task<Customer> EnsureCustomerAsync(ShopperIdentity shopper, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerByReferenceOrNullAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            await RecordCustomerLinkAsync(shopper.UserId, existing);
            return existing;
        }

        var claim = new MaxioCustomerLink
        {
            MaxioReference = shopper.UserId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        try
        {
            await _customerLinks.AddAsync(claim, cancellationToken);
        }
        catch (DbUpdateException)
        {
            var inFlight = await ReadCustomerByReferenceOrNullAsync(shopper.UserId, cancellationToken);
            if (inFlight is not null)
                return inFlight;

            throw new MaxioBillingException(
                "A customer request is already in progress; try again shortly.",
                HttpStatusCode.Conflict);
        }

        Customer? created = null;
        try
        {
            await BoundedAsync(async token =>
            {
                var response = await _client.Customers.CreateCustomer(
                    new CreateCustomerOperationRequest
                    {
                        Body = new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = shopper.FirstName,
                                LastName = shopper.LastName,
                                Email = shopper.Email,
                                Reference = shopper.UserId
                            }
                        }
                    },
                    cancellationToken: token);
                created = response.Customer;
            }, cancellationToken);

            _logger.LogInformation("Customer {Reference} created for shopper {UserId}.", shopper.UserId, shopper.UserId);
        }
        catch (ApiException<CreateCustomerError> ex)
        {
            var message = ex.Error.TryGetCustomerErrorResponse1(out var body) && body.Errors is not null
                ? $"The billing system rejected the customer: {DescribeCustomerErrors(body.Errors)}"
                : ex.Error.TryGetRawError(out var raw)
                    ? RawMessage(raw, ex.StatusCode)
                    : "The billing system returned an unrecognized error.";
            await ReleaseClaimAsync(claim);
            throw new MaxioBillingException(message, ex.StatusCode, ex);
        }
        catch (Exception ex) when (IsUnknownOutcome(ex))
        {
            // The create may still have landed — settle by reference before reporting anything.
            var settled = await ReadCustomerByReferenceOrNullAsync(shopper.UserId, cancellationToken);
            if (settled is not null)
            {
                await RecordCustomerLinkAsync(shopper.UserId, settled);
                _logger.LogInformation("Customer {Reference} was created despite the failed call.", shopper.UserId);
                return settled;
            }
            await ReleaseClaimAsync(claim);
            throw Translate(ex);
        }
        catch (Exception ex) when (ex is not MaxioBillingException and not OperationCanceledException)
        {
            await ReleaseClaimAsync(claim);
            throw Translate(ex);
        }

        await RecordCustomerLinkAsync(shopper.UserId, created!);
        return created!;
    }

    private async Task<Customer?> ReadCustomerByReferenceOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        CustomerResponse? response = null;
        try
        {
            await BoundedAsync(async token =>
            {
                response = await _client.Customers.ReadCustomerByReference(
                    new ReadCustomerByReferenceRequest { Reference = reference },
                    cancellationToken: token);
            }, cancellationToken);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // a genuine provider miss
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            throw Translate(ex);
        }

        var customer = response?.Customer;
        if (customer is null)
            _logger.LogWarning("ReadCustomerByReference({Reference}) returned an empty customer body.", reference);
        return customer;
    }

    // --- subscription lookups ---------------------------------------------------

    private async Task<Subscription?> FindSubscriptionOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        SubscriptionResponse? response = null;
        try
        {
            await BoundedAsync(async token =>
            {
                response = await _client.Subscriptions.FindSubscription(
                    new FindSubscriptionRequest { Reference = reference },
                    cancellationToken: token);
            }, cancellationToken);
        }
        catch (ApiException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null; // 404 — no subscription carries this reference
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            throw Translate(ex);
        }

        var subscription = response?.Subscription;
        if (subscription is null)
            _logger.LogWarning("FindSubscription({Reference}) returned an empty subscription body.", reference);
        return subscription;
    }

    // --- local link bookkeeping -------------------------------------------------

    private async Task RecordCustomerLinkAsync(string reference, Customer customer)
    {
        var link = await _customerLinks.FirstOrDefaultAsync(new MaxioCustomerLinkByReferenceSpec(reference));
        var now = DateTimeOffset.UtcNow;
        if (link is null)
        {
            link = new MaxioCustomerLink
            {
                MaxioReference = reference,
                MaxioCustomerId = customer.Id ?? 0,
                CreatedAt = now,
                UpdatedAt = now
            };
            try { await _customerLinks.AddAsync(link); }
            catch (DbUpdateException) { /* a concurrent writer recorded it first */ }
        }
        else
        {
            link.MaxioCustomerId = customer.Id ?? link.MaxioCustomerId;
            link.UpdatedAt = now;
            try { await _customerLinks.UpdateAsync(link); }
            catch (DbUpdateException) { /* nothing to do — the row already reflects the customer */ }
        }
    }

    private async Task RecordLinkAsync(MaxioSubscriptionLink link, Subscription subscription)
    {
        link.MaxioSubscriptionId = subscription.Id ?? 0;
        link.MaxioCustomerId = subscription.Customer?.Id ?? link.MaxioCustomerId;
        link.State = subscription.State?.Value ?? "unknown";
        link.UpdatedAt = DateTimeOffset.UtcNow;
        try { await _subscriptionLinks.UpdateAsync(link); }
        catch (DbUpdateException) { /* best effort — the provider remains the system of record */ }
    }

    private async Task ReleaseClaimAsync(MaxioCustomerLink claim)
    {
        try { await _customerLinks.DeleteAsync(claim); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not release customer claim {Reference}.", claim.MaxioReference);
        }
    }

    private async Task ReleaseClaimAsync(MaxioSubscriptionLink claim)
    {
        try { await _subscriptionLinks.DeleteAsync(claim); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not release subscription claim {Reference}.", claim.MaxioReference);
        }
    }

    private async Task<SubscriptionInfo> SettleUnknownCreateOutcomeAsync(
        MaxioSubscriptionLink claim, string reference, string productHandle, Exception cause, CancellationToken cancellationToken)
    {
        var settled = await FindSubscriptionOrNullAsync(reference, cancellationToken);
        if (settled is not null)
        {
            await RecordLinkAsync(claim, settled);
            _logger.LogWarning(
                cause,
                "Subscription {Reference} was created despite the failed call; outcome settled from the provider.",
                reference);
            return MapSubscription(settled, productHandle);
        }

        await ReleaseClaimAsync(claim);
        throw new MaxioBillingException(
            "The billing system did not answer while creating the subscription; the outcome was reconciled as not created. Retry the request.",
            null, cause);
    }

    // --- mapping ----------------------------------------------------------------

    private static SubscriptionInfo MapSubscription(Subscription subscription, string productHandle) =>
        new(
            subscription.Id ?? 0,
            subscription.Product?.Handle ?? productHandle,
            subscription.Product?.Name,
            subscription.State?.Value ?? "unknown",
            subscription.ProductPriceInCents,
            subscription.CurrentPeriodEndsAt,
            subscription.Reference ?? string.Empty);

    private static SubscriptionPlanInfo MapPlan(Product product) =>
        new(
            product.Handle ?? string.Empty,
            product.Name ?? product.Handle ?? string.Empty,
            product.Description,
            product.PriceInCents ?? 0,
            product.Interval ?? 1,
            product.IntervalUnit?.Value ?? "month",
            product.RequireCreditCard ?? false);

    // --- failure handling -------------------------------------------------------

    private static bool IsEndOfLife(string? state) =>
        state is not null && EndOfLifeStates.Contains(state, StringComparer.Ordinal);

    /// <summary>The provider answered with something the SDK raised as an API error.</summary>
    private static bool IsProviderFailure(Exception ex) =>
        ex is ApiException or ResponseDeserializationException
            && ex is not MaxioBillingException
            && ex is not OperationCanceledException;

    /// <summary>
    /// The write may still have landed: the transport failed (timeout / connection) or a 2xx body
    /// could not be read. Those outcomes are settled by re-reading the provider, never reported as
    /// plain failures.
    /// </summary>
    private static bool IsUnknownOutcome(Exception ex) =>
        ex is SdkTimeoutException
            || ex is SdkConnectionException
            || (ex is ResponseDeserializationException rde && (int)rde.StatusCode is >= 200 and < 300);

    private static MaxioBillingException RawError(RawError raw, HttpStatusCode? status, Exception inner) =>
        new(RawMessage(raw, status), status, inner);

    private static string RawMessage(RawError raw, HttpStatusCode? status) =>
        $"The billing system returned HTTP {(int?)status ?? (int)raw.StatusCode}.";

    /// <summary>Single shared conversion of SDK failures into the caller-facing exception type.</summary>
    private static MaxioBillingException Translate(Exception ex) => ex switch
    {
        // Our credentials or our quota — the caller did nothing wrong and cannot fix it.
        ApiException<RawError> api when api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            => new("The billing system rejected the integration's credentials.", api.StatusCode, api),
        ApiException<RawError> api when api.StatusCode == HttpStatusCode.TooManyRequests
            => new("The billing system is rate limiting requests.", api.StatusCode, api),
        // The provider rejected the caller's request — hand back the same status.
        ApiException<RawError> api when api.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
            => new($"The billing system rejected the request (HTTP {(int)api.StatusCode}).", api.StatusCode, api),
        // Transport, provider 5xx, unknown.
        ApiException<RawError> api
            => new($"The billing system returned an error (HTTP {(int)api.StatusCode}).", api.StatusCode, api),
        ResponseDeserializationException rde
            => new($"The billing system returned a response that could not be processed (HTTP {(int?)rde.StatusCode ?? 0}).", rde.StatusCode, rde),
        SdkTimeoutException
            => new("The billing system did not answer in time.", null, ex),
        SdkConnectionException
            => new("The billing system could not be reached.", null, ex),
        AuthSchemeException
            => new("The billing system credentials could not be applied.", null, ex),
        _ => new("An unexpected billing system error occurred.", null, ex)
    };

    private static string DescribeCustomerErrors(Errors1 errors)
    {
        // Errors1 (Models/AnyOf/Errors1.cs) is read via TryGet… accessors, not Match.
        if (errors.TryGetListOfString(out var list))
            return string.Join("; ", list);
        if (errors.TryGetCustomerError(out var single))
            return single.Customer ?? "customer rejected";
        return "customer rejected";
    }

    private async Task BoundedAsync(Func<CancellationToken, Task> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TotalCallBudget);
        await call(cts.Token);
    }
}