using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PageSize = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> IntentLocks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly MaxioCallContext _callContext;
    private readonly CatalogContext _db;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        MaxioCallContext callContext,
        CatalogContext db,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _callContext = callContext;
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var families = await ListProductFamiliesAsync(cancellationToken);
        var family = families
            .Select(x => x.ProductFamily)
            .FirstOrDefault(x => x is not null &&
                                 string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));

        if (family?.Id is not int familyId)
        {
            throw new BillingException(
                BillingFailureKind.Configuration,
                "The configured Maxio product family was not found.");
        }

        var plans = new List<SubscriptionPlan>();
        for (var page = 1; ; page++)
        {
            var products = await ListProductsAsync(familyId, page, cancellationToken);
            foreach (var envelope in products)
            {
                var product = envelope.Product;
                if (product.Id is not int productId ||
                    string.IsNullOrWhiteSpace(product.Handle) ||
                    string.IsNullOrWhiteSpace(product.Name) ||
                    product.PriceInCents is not long priceInCents ||
                    product.Interval is not int interval ||
                    product.IntervalUnit is null ||
                    product.DefaultProductPricePointId is not int pricePointId)
                {
                    throw MalformedResponse("Maxio returned an incomplete product.");
                }

                plans.Add(new SubscriptionPlan(
                    productId,
                    product.Handle,
                    product.Name,
                    product.Description,
                    priceInCents,
                    interval,
                    product.IntervalUnit.Value,
                    pricePointId,
                    product.ProductPricePointHandle,
                    product.ProductPricePointName));
            }

            if (products.Count < PageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<SubscriptionDetails> SubscribeAsync(
        BillingIdentity identity,
        int productId,
        CancellationToken cancellationToken)
    {
        if (productId <= 0)
        {
            throw new BillingException(BillingFailureKind.Validation, "A valid productId is required.");
        }

        var lockKey = string.Concat(identity.UserId, ":", productId.ToString(CultureInfo.InvariantCulture));
        var intentLock = IntentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await intentLock.WaitAsync(cancellationToken);

        try
        {
            var plans = await ListPlansAsync(cancellationToken);
            if (plans.All(x => x.ProductId != productId))
            {
                throw new BillingException(
                    BillingFailureKind.Validation,
                    "The selected product is not an available subscription plan.");
            }

            var providerReference = BuildSubscriptionReference(identity.UserId, productId);
            var intent = await _db.SubscriptionIntents
                .SingleOrDefaultAsync(x => x.UserId == identity.UserId && x.ProductId == productId, cancellationToken);

            var ownsIntent = false;
            if (intent is null)
            {
                intent = new SubscriptionIntent(identity.UserId, productId, providerReference);
                _db.SubscriptionIntents.Add(intent);
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    ownsIntent = true;
                }
                catch (DbUpdateException)
                {
                    _db.Entry(intent).State = EntityState.Detached;
                    intent = await _db.SubscriptionIntents.SingleAsync(
                        x => x.UserId == identity.UserId && x.ProductId == productId,
                        cancellationToken);
                }
            }

            if (!ownsIntent)
            {
                return await ReplayOrReconcileAsync(intent, cancellationToken);
            }

            var writeStarted = false;
            try
            {
                var reconciled = await FindSubscriptionAsync(providerReference, cancellationToken);
                if (reconciled is not null)
                {
                    intent.MarkSucceeded(reconciled);
                    await _db.SaveChangesAsync(cancellationToken);
                    return reconciled;
                }

                await EnsureCustomerAsync(identity, cancellationToken);
                writeStarted = true;

                var created = await CreateSubscriptionAsync(
                    productId,
                    BuildCustomerReference(identity.UserId),
                    providerReference,
                    cancellationToken);

                intent.MarkSucceeded(created);
                await _db.SaveChangesAsync(cancellationToken);
                return created;
            }
            catch (BillingException ex)
            {
                if (writeStarted && ex.Kind is BillingFailureKind.Unavailable or BillingFailureKind.UnknownOutcome)
                {
                    var reconciled = await TryReconcileAfterAmbiguousWriteAsync(providerReference);
                    if (reconciled is not null)
                    {
                        intent.MarkSucceeded(reconciled);
                        await PersistIntentAsync();
                        return reconciled;
                    }
                }

                if (!writeStarted)
                {
                    _db.SubscriptionIntents.Remove(intent);
                }
                else if (ex.Kind == BillingFailureKind.Validation)
                {
                    intent.MarkFailed();
                }
                else
                {
                    intent.MarkUnknown();
                }

                await PersistIntentAsync();
                throw;
            }
            catch (OperationCanceledException) when (writeStarted)
            {
                intent.MarkUnknown();
                await PersistIntentAsync();
                throw;
            }
        }
        finally
        {
            intentLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(
        BillingIdentity identity,
        CancellationToken cancellationToken)
    {
        var customer = await ReadCustomerAsync(BuildCustomerReference(identity.UserId), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDetails>();
        }

        if (customer.Id is not int customerId)
        {
            throw MalformedResponse("Maxio returned an incomplete customer.");
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return subscriptions.Select(MapSubscription).ToArray();
    }

    private async Task<SubscriptionDetails> ReplayOrReconcileAsync(
        SubscriptionIntent intent,
        CancellationToken cancellationToken)
    {
        if (intent.Status == SubscriptionIntentStatus.Succeeded)
        {
            return intent.ToSubscriptionDetails();
        }

        if (intent.Status == SubscriptionIntentStatus.Failed)
        {
            throw new BillingException(
                BillingFailureKind.Validation,
                "The previous subscription request for this plan was rejected by Maxio.",
                HttpStatusCode.UnprocessableEntity);
        }

        var reconciled = await FindSubscriptionAsync(intent.ProviderReference, cancellationToken);
        if (reconciled is not null)
        {
            intent.MarkSucceeded(reconciled);
            await _db.SaveChangesAsync(cancellationToken);
            return reconciled;
        }

        if (intent.Status == SubscriptionIntentStatus.Pending)
        {
            throw new BillingException(
                BillingFailureKind.Conflict,
                "A subscription request for this plan is already being processed.");
        }

        throw new BillingException(
            BillingFailureKind.UnknownOutcome,
            "The earlier subscription request has an unknown outcome and requires reconciliation.");
    }

    private async Task<Customer> EnsureCustomerAsync(
        BillingIdentity identity,
        CancellationToken cancellationToken)
    {
        var reference = BuildCustomerReference(identity.UserId);
        var existing = await ReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await CreateCustomerAsync(identity, reference, cancellationToken);
        }
        catch (BillingException ex) when (ex.Kind == BillingFailureKind.Validation)
        {
            var concurrent = await ReadCustomerAsync(reference, cancellationToken);
            if (concurrent is not null)
            {
                return concurrent;
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<ProductFamilyResponse>> ListProductFamiliesAsync(CancellationToken ct)
    {
        using var budget = CreateBudget(ct);
        using var call = _callContext.Begin(writeOnce: false);
        try
        {
            return await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: budget.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "Unable to load Maxio product families.", ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundaryFailure(ex, write: false);
        }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(int familyId, int page, CancellationToken ct)
    {
        using var budget = CreateBudget(ct);
        using var call = _callContext.Begin(writeOnce: false);
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: page,
                perPage: PageSize,
                ct: budget.Token);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new BillingException(BillingFailureKind.NotFound, "The Maxio product family was not found.", HttpStatusCode.NotFound, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Unable to load Maxio products.", ex);
            }

            throw ProviderUnavailable("Unable to load Maxio products.", ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundaryFailure(ex, write: false);
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken ct)
    {
        using var budget = CreateBudget(ct);
        using var call = _callContext.Begin(writeOnce: false);
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: budget.Token);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "Unable to read the Maxio customer.", ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundaryFailure(ex, write: false);
        }
    }

    private async Task<Customer> CreateCustomerAsync(
        BillingIdentity identity,
        string reference,
        CancellationToken ct)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = identity.FirstName,
                LastName = identity.LastName,
                Email = identity.Email,
                Reference = reference
            }
        };

        using var budget = CreateBudget(ct);
        using var call = _callContext.Begin(writeOnce: true);
        try
        {
            var response = await _client.Customers.CreateCustomer(body, ct: budget.Token);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new BillingException(BillingFailureKind.Validation, "Maxio rejected the customer profile.", HttpStatusCode.UnprocessableEntity, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Unable to create the Maxio customer.", ex);
            }

            throw ProviderUnavailable("Unable to create the Maxio customer.", ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundaryFailure(ex, write: true);
        }
    }

    private async Task<SubscriptionDetails?> FindSubscriptionAsync(string reference, CancellationToken ct)
    {
        using var budget = CreateBudget(ct);
        using var call = _callContext.Begin(writeOnce: false);
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, ct: budget.Token);
            return response.Subscription is null ? null : MapSubscription(response);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Unable to reconcile the Maxio subscription.", ex);
            }

            throw ProviderUnavailable("Unable to reconcile the Maxio subscription.", ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundaryFailure(ex, write: false);
        }
    }

    private async Task<SubscriptionDetails> CreateSubscriptionAsync(
        int productId,
        string customerReference,
        string subscriptionReference,
        CancellationToken ct)
    {
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductId = productId,
                CustomerReference = customerReference,
                Reference = subscriptionReference
            }
        };

        using var budget = CreateBudget(ct);
        using var call = _callContext.Begin(writeOnce: true);
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: budget.Token);
            return MapSubscription(response);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                _logger.LogWarning(
                    "Maxio rejected subscription creation with HTTP 422. Validation errors: {ValidationErrors}",
                    FormatValidationErrors(validation.Errors));

                throw new BillingException(BillingFailureKind.Validation, "Maxio rejected the subscription request.", HttpStatusCode.UnprocessableEntity, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "Unable to create the Maxio subscription.", ex);
            }

            throw ProviderUnavailable("Unable to create the Maxio subscription.", ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundaryFailure(ex, write: true);
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        using var budget = CreateBudget(ct);
        using var call = _callContext.Begin(writeOnce: false);
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct: budget.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "Unable to load Maxio subscriptions.", ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw FromBoundaryFailure(ex, write: false);
        }
    }

    private static SubscriptionDetails MapSubscription(SubscriptionResponse response)
    {
        var subscription = response.Subscription;
        if (subscription?.Id is not int id ||
            subscription.Product is null ||
            string.IsNullOrWhiteSpace(subscription.Product.Name) ||
            string.IsNullOrWhiteSpace(subscription.Product.Handle))
        {
            throw MalformedResponse("Maxio returned an incomplete subscription.");
        }

        return new SubscriptionDetails(
            id,
            subscription.Reference,
            subscription.Product.Name,
            subscription.Product.Handle,
            subscription.ProductPriceInCents,
            subscription.State?.Value,
            subscription.NextAssessmentAt);
    }

    private async Task<SubscriptionDetails?> TryReconcileAfterAmbiguousWriteAsync(string reference)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await FindSubscriptionAsync(reference, timeout.Token);
        }
        catch (Exception ex) when (ex is BillingException or OperationCanceledException)
        {
            _logger.LogWarning("Maxio subscription reconciliation did not establish an outcome.");
            return null;
        }
    }

    private async Task PersistIntentAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _db.SaveChangesAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist a Maxio subscription intent outcome.");
        }
    }

    private static string BuildCustomerReference(string userId) => "eshop-customer-" + Hash(userId);

    private static string BuildSubscriptionReference(string userId, int productId) =>
        "eshop-subscription-" + Hash(string.Concat(userId, "\n", productId.ToString(CultureInfo.InvariantCulture)));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static CancellationTokenSource CreateBudget(CancellationToken ct)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(CallBudget);
        return budget;
    }

    private static bool IsBoundaryFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or MaxioWriteRetryBlockedException;

    private BillingException FromBoundaryFailure(Exception ex, bool write)
    {
        if (ex is MaxioWriteRetryBlockedException)
        {
            return new BillingException(
                BillingFailureKind.UnknownOutcome,
                "The Maxio write may have completed; its outcome could not be confirmed.",
                innerException: ex);
        }

        if (ex is JsonException && _callContext.LastStatusCode is { } status && (int)status is >= 400 and < 500)
        {
            return new BillingException(
                BillingFailureKind.Validation,
                "Maxio rejected the request, but its error response could not be processed.",
                status,
                ex);
        }

        return new BillingException(
            write ? BillingFailureKind.UnknownOutcome : BillingFailureKind.Unavailable,
            write
                ? "The Maxio write outcome could not be confirmed."
                : "Maxio is currently unavailable.",
            innerException: ex);
    }

    private static BillingException FromRaw(RawError raw, string message, Exception inner)
    {
        var status = raw.StatusCode;
        var kind = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => BillingFailureKind.Authentication,
            HttpStatusCode.NotFound => BillingFailureKind.NotFound,
            HttpStatusCode.Conflict => BillingFailureKind.Conflict,
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => BillingFailureKind.Validation,
            _ when (int)status >= 400 && (int)status < 500 => BillingFailureKind.Validation,
            _ => BillingFailureKind.Unavailable
        };

        return new BillingException(kind, message, status, inner);
    }

    private static BillingException ProviderUnavailable(string message, Exception inner) =>
        new(BillingFailureKind.Unavailable, message, innerException: inner);

    private static string FormatValidationErrors(IReadOnlyList<string> errors)
    {
        var safeErrors = errors
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .Take(10)
            .Select(error => error
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim())
            .Select(error => error.Length <= 500 ? error : error[..500] + "…")
            .ToArray();

        return safeErrors.Length == 0
            ? "(provider returned an empty validation list)"
            : string.Join(" | ", safeErrors);
    }

    private static BillingException MalformedResponse(string message) =>
        new(BillingFailureKind.Unavailable, message);
}
