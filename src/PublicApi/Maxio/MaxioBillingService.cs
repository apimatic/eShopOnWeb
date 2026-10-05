using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The Maxio Advanced Billing integration boundary. Every SDK call lives here; all
/// failures surface as <see cref="MaxioBillingException"/>. Maxio writes are made
/// idempotent with deterministic references plus look-before-create reads, backed by
/// per-user in-process locks and primary-key claim rows (see maxio-advanced-billing-plan.md).
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private const int PlansPerPage = 200;
    private const int MaxPlanPages = 25;
    private const string ReferencePrefix = "eshopweb";
    private const string FallbackLastName = "Customer";
    private const string FallbackFirstName = "eShop";
    private const string FallbackEmailDomain = "eshoponweb.local";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IRepository<MaxioCustomerLink> _linkRepository;
    private readonly IRepository<MaxioSubscriptionClaim> _claimRepository;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IRepository<MaxioCustomerLink> linkRepository,
        IRepository<MaxioSubscriptionClaim> claimRepository,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _linkRepository = linkRepository;
        _claimRepository = claimRepository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MaxioPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        using var budget = BeginFlowBudget(cancellationToken);
        return await ListPlansCoreAsync(budget.Token);
    }

    public async Task<MaxioSubscriptionDto> SubscribeAsync(MaxioSubscriberIdentity subscriber, string productHandle, CancellationToken cancellationToken)
    {
        if (subscriber is null || string.IsNullOrWhiteSpace(subscriber.UserId))
        {
            throw new MaxioBillingException(MaxioBillingException.FailureKind.RequestRejected, "The caller's identity could not be resolved from the token.", 401);
        }
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException(MaxioBillingException.FailureKind.RequestRejected, "productHandle is required.", 400);
        }

        using var budget = BeginFlowBudget(cancellationToken);
        var ct = budget.Token;

        // Cross-operation invariant: the requested handle must be one the configured
        // family's plan list returned — never a value taken on trust from the caller.
        var plans = await ListPlansCoreAsync(ct);
        var product = plans.FirstOrDefault(plan =>
            string.Equals(plan.Handle, productHandle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            throw new MaxioBillingException(MaxioBillingException.FailureKind.RequestRejected,
                $"Subscription plan '{productHandle.Trim()}' was not found.", 404);
        }
        var handle = product.Handle;

        // One flow per user: the ensure-customer and subscribe sequence is serialized so
        // a double-click cannot interleave two provider flows for the same shopper.
        var userLock = UserLocks.GetOrAdd(subscriber.UserId, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(ct);
        try
        {
            var customerId = await EnsureCustomerAsync(subscriber, ct);
            var reference = SubscriptionReference(subscriber.UserId, handle);

            var existing = await FindSubscriptionOrNullAsync(reference, ct);
            if (existing is not null)
            {
                _logger.LogInformation("Idempotent subscribe: subscription {Reference} already exists (state {State}).", reference, SubscriptionStateWire(existing));
                return MapSubscription(existing, created: false);
            }

            var claim = new MaxioSubscriptionClaim
            {
                RequestId = reference,
                UserId = subscriber.UserId,
                ProductHandle = handle,
                Status = MaxioSubscriptionClaim.StatusPending,
                CreatedUtc = DateTimeOffset.UtcNow
            };
            try
            {
                await _claimRepository.AddAsync(claim, ct);
            }
            catch (DbUpdateException)
            {
                // The store rejected the second claim. Settle from the provider before refusing:
                // the winner may have finished (or the outcome may be pending on an Unknown row).
                var winner = await FindSubscriptionOrNullAsync(reference, ct);
                if (winner is not null)
                {
                    return MapSubscription(winner, created: false);
                }
                throw new MaxioBillingException(MaxioBillingException.FailureKind.ProviderUnavailable,
                    "A request for this subscription is already in progress or its outcome is pending settlement.", 409);
            }

            Subscription subscription;
            try
            {
                var response = await _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionOperationRequest
                    {
                        Body = new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = handle,
                                CustomerId = customerId,
                                Reference = reference,
                                // The seeded plans require no payment method; remittance
                                // (invoice-style collection) signs the shopper up without
                                // card capture — automatic would demand a card for the
                                // initial balance.
                                PaymentCollectionMethod = CollectionMethod.Remittance
                            }
                        }
                    },
                    cancellationToken: ct);
                subscription = response.Subscription
                    ?? throw new MaxioBillingException(MaxioBillingException.FailureKind.ProviderUnavailable,
                        "Maxio accepted the subscription but returned no subscription body.");
            }
            catch (ApiException<CreateSubscriptionError> ex)
            {
                // Case A — cover every declared accessor, raw fallback last.
                if (ex.Error.TryGetErrorListResponse1(out var list))
                {
                    _logger.LogWarning("Maxio rejected subscription creation for {Reference}: {Errors}", reference, string.Join("; ", list.Errors));
                    throw new MaxioBillingException(MaxioBillingException.FailureKind.RequestRejected,
                        string.Join("; ", list.Errors), (int)ex.StatusCode, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new MaxioBillingException(KindFor(ex.StatusCode), $"Maxio rejected the subscription request: {Truncate(raw.ReadAsString())}", (int)ex.StatusCode, ex);
                }
                throw new MaxioBillingException(MaxioBillingException.FailureKind.ProviderUnavailable,
                    "Maxio returned an unrecognised error shape.", (int)ex.StatusCode, ex);
            }
            catch (ResponseDeserializationException ex) when (IsSuccessStatus(ex.StatusCode))
            {
                // A 2xx body that could not be read — the outcome is unknown until re-read.
                var settled = await FindSubscriptionOrNullAsync(reference, ct);
                if (settled is not null)
                {
                    await ConfirmClaimAsync(claim, settled, ct);
                    return MapSubscription(settled, created: true);
                }
                await MarkClaimUnknownAsync(claim, ct);
                throw new MaxioBillingException(MaxioBillingException.FailureKind.UnknownOutcome,
                    "Maxio may have created the subscription; its response could not be read and no subscription was found for the reference.", null, ex);
            }
            catch (ResponseDeserializationException ex)
            {
                // The provider rejected the request and only the error detail was lost — keep the status.
                throw new MaxioBillingException(KindFor(ex.StatusCode),
                    "Maxio rejected the subscription request and its error body could not be processed.", (int)ex.StatusCode, ex);
            }
            catch (SdkException ex) when (ex is SdkTimeoutException or SdkConnectionException)
            {
                // The connection failed after the write may have landed — settle the outcome here.
                var settled = await FindSubscriptionOrNullAsync(reference, ct);
                if (settled is not null)
                {
                    await ConfirmClaimAsync(claim, settled, ct);
                    return MapSubscription(settled, created: true);
                }
                await MarkClaimUnknownAsync(claim, ct);
                _logger.LogError(ex, "Maxio subscription creation for {Reference} has an unknown outcome; the claim row is marked Unknown.", reference);
                throw new MaxioBillingException(MaxioBillingException.FailureKind.UnknownOutcome,
                    "The subscription request to Maxio did not complete and its outcome is unknown. The request will be settled by reference on the next attempt.", null, ex);
            }
            catch (SdkException ex)
            {
                throw MaxioBillingException.FromSdkException(ex);
            }

            await ConfirmClaimAsync(claim, subscription, ct);
            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for {Reference} (plan {PlanHandle}).",
                subscription.Id, reference, handle);
            return MapSubscription(subscription, created: true);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> ListMySubscriptionsAsync(MaxioSubscriberIdentity subscriber, CancellationToken cancellationToken)
    {
        if (subscriber is null || string.IsNullOrWhiteSpace(subscriber.UserId))
        {
            throw new MaxioBillingException(MaxioBillingException.FailureKind.RequestRejected, "The caller's identity could not be resolved from the token.", 401);
        }

        using var budget = BeginFlowBudget(cancellationToken);
        var ct = budget.Token;

        var customerId = await ResolveCustomerIdReadOnlyAsync(subscriber, ct);
        if (customerId is null)
        {
            return Array.Empty<MaxioSubscriptionDto>();
        }

        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId.Value },
                cancellationToken: ct);
        }
        catch (ApiException<RawError> ex)
        {
            throw new MaxioBillingException(KindFor(ex.StatusCode),
                $"Maxio returned HTTP {(int)ex.StatusCode} listing subscriptions.", (int)ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            throw MaxioBillingException.FromSdkException(ex);
        }

        return subscriptions.Select(item => MapSubscription(item.Subscription!, created: false)).ToList();
    }

    // ----- plans -----

    private async Task<IReadOnlyList<MaxioPlanDto>> ListPlansCoreAsync(CancellationToken ct)
    {
        var familyId = $"handle:{_options.ProductFamilyHandle}";
        var plans = new List<MaxioPlanDto>();
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest
                    {
                        ProductFamilyId = familyId,
                        Page = page,
                        PerPage = PlansPerPage
                    },
                    cancellationToken: ct);
            }
            catch (ApiException<ListProductsForProductFamilyError> ex)
            {
                // Case A — cover every declared accessor, raw fallback last.
                if (ex.Error.TryGetString(out var notFound))
                {
                    throw new MaxioBillingException(MaxioBillingException.FailureKind.RequestRejected,
                        $"The configured Maxio product family '{_options.ProductFamilyHandle}' was not found: {Truncate(notFound)}", (int)ex.StatusCode, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new MaxioBillingException(KindFor(ex.StatusCode),
                        $"Maxio returned HTTP {(int)ex.StatusCode} listing plans: {Truncate(raw.ReadAsString())}", (int)ex.StatusCode, ex);
                }
                throw new MaxioBillingException(MaxioBillingException.FailureKind.ProviderUnavailable,
                    "Maxio returned an unrecognised error shape.", (int)ex.StatusCode, ex);
            }
            catch (SdkException ex)
            {
                throw MaxioBillingException.FromSdkException(ex);
            }

            foreach (var item in products)
            {
                var p = item.Product;
                if (p.ArchivedAt is null)
                {
                    plans.Add(MapPlan(p));
                }
            }

            if (products.Count < PlansPerPage)
            {
                return plans;
            }
            if (page >= MaxPlanPages)
            {
                // A silently truncated plan list is a wrong answer — refuse rather than return it.
                throw new MaxioBillingException(MaxioBillingException.FailureKind.ProviderUnavailable,
                    "The Maxio plan catalogue did not terminate within the page cap; refusing to return a possibly truncated plan list.");
            }
        }
    }

    private static MaxioPlanDto MapPlan(Product product)
    {
        return new MaxioPlanDto
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value,
            RequiresPaymentMethod = product.RequireCreditCard == true
        };
    }

    // ----- customers -----

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper: local link first, then a lookup by
    /// the customer reference (= user id), then creation. The link row's primary key is the
    /// user id, so a concurrent second claim is rejected by the store.
    /// </summary>
    private async Task<int> EnsureCustomerAsync(MaxioSubscriberIdentity subscriber, CancellationToken ct)
    {
        var link = await FindLinkAsync(subscriber.UserId, ct);
        if (link is not null)
        {
            return link.MaxioCustomerId;
        }

        var customer = await ReadCustomerByReferenceOrNullAsync(subscriber.UserId, ct);
        if (customer is null)
        {
            var (firstName, lastName) = SplitName(subscriber.UserName);
            var email = string.IsNullOrWhiteSpace(subscriber.Email)
                ? $"{subscriber.UserId}@{FallbackEmailDomain}"
                : subscriber.Email!;
            try
            {
                customer = await _client.Customers.CreateCustomer(
                    new CreateCustomerOperationRequest
                    {
                        Body = new CreateCustomerRequest
                        {
                            Customer = new CreateCustomer
                            {
                                FirstName = firstName,
                                LastName = lastName,
                                Email = email,
                                Reference = subscriber.UserId
                            }
                        }
                    },
                    cancellationToken: ct);
            }
            catch (ApiException<CreateCustomerError> ex)
            {
                // Case A — cover every declared accessor, raw fallback last.
                if (ex.Error.TryGetCustomerErrorResponse1(out var typed))
                {
                    var message = MaxioBillingException.DescribeCreateCustomerError(ex.Error, string.Empty);
                    _logger.LogWarning("Maxio rejected customer creation for user {UserId}: {Message}", subscriber.UserId, message);
                    throw new MaxioBillingException(MaxioBillingException.FailureKind.RequestRejected, message, (int)ex.StatusCode, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new MaxioBillingException(KindFor(ex.StatusCode),
                        $"Maxio rejected the customer request: {Truncate(raw.ReadAsString())}", (int)ex.StatusCode, ex);
                }
                throw new MaxioBillingException(MaxioBillingException.FailureKind.ProviderUnavailable,
                    "Maxio returned an unrecognised error shape.", (int)ex.StatusCode, ex);
            }
            catch (SdkException ex) when (ex is SdkTimeoutException or SdkConnectionException)
            {
                // The create may have landed — settle by reference before reporting failure.
                customer = await ReadCustomerByReferenceOrNullAsync(subscriber.UserId, ct);
                if (customer is null)
                {
                    throw new MaxioBillingException(MaxioBillingException.FailureKind.UnknownOutcome,
                        "The Maxio customer request did not complete and its outcome is unknown; it will be settled by reference on the next attempt.", null, ex);
                }
            }
            catch (SdkException ex)
            {
                throw MaxioBillingException.FromSdkException(ex);
            }
        }

        var customerId = customer.Customer.Id
            ?? throw new MaxioBillingException(MaxioBillingException.FailureKind.ProviderUnavailable, "Maxio returned a customer without an id.");

        var newLink = new MaxioCustomerLink
        {
            UserId = subscriber.UserId,
            MaxioCustomerId = customerId,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        try
        {
            await _linkRepository.AddAsync(newLink, ct);
        }
        catch (DbUpdateException)
        {
            // The store rejected a concurrent second link claim — continue with the winner's link.
            link = await FindLinkAsync(subscriber.UserId, ct);
            if (link is not null)
            {
                return link.MaxioCustomerId;
            }
        }

        return customerId;
    }

    /// <summary>
    /// Resolves the Maxio customer id without ever creating one. Returns null when no
    /// customer exists for the shopper (the "my subscriptions" list is then empty).
    /// </summary>
    private async Task<int?> ResolveCustomerIdReadOnlyAsync(MaxioSubscriberIdentity subscriber, CancellationToken ct)
    {
        var link = await FindLinkAsync(subscriber.UserId, ct);
        if (link is not null)
        {
            return link.MaxioCustomerId;
        }

        var customer = await ReadCustomerByReferenceOrNullAsync(subscriber.UserId, ct);
        return customer?.Customer.Id;
    }

    private async Task<MaxioCustomerLink?> FindLinkAsync(string userId, CancellationToken ct)
    {
        return await _linkRepository.FirstOrDefaultAsync(new MaxioCustomerLinkByUserIdSpecification(userId), ct);
    }

    private async Task<CustomerResponse?> ReadCustomerByReferenceOrNullAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ReadCustomerByReference(
                new ReadCustomerByReferenceRequest { Reference = reference },
                cancellationToken: ct);
        }
        catch (ApiException<RawError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Expected first-contact path: the shopper has no Maxio customer yet.
            return null;
        }
        catch (SdkException ex)
        {
            throw MaxioBillingException.FromSdkException(ex);
        }
    }

    // ----- subscriptions -----

    /// <summary>
    /// Looks a subscription up by this app's deterministic reference. Returns null when
    /// Maxio holds no subscription for that reference (404 — the expected first-contact path).
    /// </summary>
    private async Task<Subscription?> FindSubscriptionOrNullAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                new FindSubscriptionRequest { Reference = reference },
                cancellationToken: ct);
            return response.Subscription;
        }
        catch (ApiException<FindSubscriptionError> ex)
        {
            // Case A — cover every declared accessor, raw fallback last.
            if (ex.Error.TryGetNoContent(out _))
            {
                return null; // 404: no subscription with this reference
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioBillingException(KindFor(ex.StatusCode),
                    $"Maxio returned HTTP {(int)ex.StatusCode} finding subscription {reference}: {Truncate(raw.ReadAsString())}", (int)ex.StatusCode, ex);
            }
            throw new MaxioBillingException(MaxioBillingException.FailureKind.ProviderUnavailable,
                "Maxio returned an unrecognised error shape.", (int)ex.StatusCode, ex);
        }
        catch (SdkException ex)
        {
            throw MaxioBillingException.FromSdkException(ex);
        }
    }

    private async Task ConfirmClaimAsync(MaxioSubscriptionClaim claim, Subscription subscription, CancellationToken ct)
    {
        claim.Status = MaxioSubscriptionClaim.StatusConfirmed;
        claim.MaxioSubscriptionId = subscription.Id;
        claim.UpdatedUtc = DateTimeOffset.UtcNow;
        try
        {
            await _claimRepository.UpdateAsync(claim, ct);
        }
        catch (DbUpdateException ex)
        {
            // Claim bookkeeping is best-effort; the provider state is authoritative.
            _logger.LogWarning(ex, "Could not persist the claim confirmation for {RequestId}.", claim.RequestId);
        }
    }

    private async Task MarkClaimUnknownAsync(MaxioSubscriptionClaim claim, CancellationToken ct)
    {
        claim.Status = MaxioSubscriptionClaim.StatusUnknown;
        claim.UpdatedUtc = DateTimeOffset.UtcNow;
        try
        {
            await _claimRepository.UpdateAsync(claim, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Could not persist the Unknown claim state for {RequestId}.", claim.RequestId);
        }
    }

    // ----- mapping & helpers -----

    private static MaxioSubscriptionDto MapSubscription(Subscription subscription, bool created)
    {
        return new MaxioSubscriptionDto
        {
            MaxioSubscriptionId = subscription.Id ?? 0,
            Reference = subscription.Reference ?? string.Empty,
            State = SubscriptionStateWire(subscription),
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            Interval = subscription.Product?.Interval,
            IntervalUnit = subscription.Product?.IntervalUnit?.Value,
            NextBillingDateUtc = subscription.CurrentPeriodEndsAt,
            ActivatedAtUtc = subscription.ActivatedAt,
            Currency = subscription.Currency,
            Created = created
        };
    }

    /// <summary>
    /// Wire value of the subscription state. OpenStringEnum values must be read through
    /// .Value — never ToString or interpolation, which carry the record's debug form.
    /// </summary>
    private static string SubscriptionStateWire(Subscription subscription)
    {
        return subscription.State?.Value ?? "unknown";
    }

    /// <summary>
    /// "eshopweb:{userId}:{productHandle}" — the only subscription reference this app
    /// ever sends, and the key every reconciliation reads back by.
    /// </summary>
    internal static string SubscriptionReference(string userId, string productHandle)
    {
        return $"{ReferencePrefix}:{userId}:{productHandle}";
    }

    private static (string FirstName, string LastName) SplitName(string? userName)
    {
        var trimmed = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();
        if (trimmed is null)
        {
            return (FallbackFirstName, FallbackLastName);
        }
        var localPart = trimmed.Split('@')[0];
        var first = string.IsNullOrWhiteSpace(localPart) ? FallbackFirstName : localPart;
        return (first, FallbackLastName);
    }

    private static MaxioBillingException.FailureKind KindFor(HttpStatusCode statusCode)
    {
        return statusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError
            ? MaxioBillingException.FailureKind.RequestRejected
            : MaxioBillingException.FailureKind.ProviderUnavailable;
    }

    private static bool IsSuccessStatus(HttpStatusCode? statusCode)
    {
        return statusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices;
    }

    private static string Truncate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "no body";
        }
        return text.Length <= 500 ? text : text[..500] + "…";
    }

    /// <summary>
    /// One deadline for the whole billing flow (a flow makes several Maxio calls; the
    /// per-attempt timeout does not bound their sum). Links the caller's cancellation
    /// so a disconnected client also stops the outbound work.
    /// </summary>
    private static CancellationTokenSource BeginFlowBudget(CancellationToken callerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(MaxioBillingRegistration.FlowBudget);
        return cts;
    }
}