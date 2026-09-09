using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Application-facing Maxio Advanced Billing boundary. Every SDK call goes through
/// <see cref="CallAsync{T}"/> (total-call budget plus transport/parse translation), and every
/// per-operation typed error is converted into <see cref="MaxioBillingException"/> carrying
/// the provider status. The caller-facing message is always caller-safe.
/// </summary>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int ProviderNotFoundStatus = 404;
    private const int ProviderValidationStatus = 422;
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PlanCacheLifetime = TimeSpan.FromMinutes(5);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    private readonly SemaphoreSlim _plansGate = new(1, 1);
    private IReadOnlyList<SubscriptionPlan>? _cachedPlans;
    private DateTimeOffset _cachedPlansAt;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        Microsoft.Extensions.Options.IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var cached = _cachedPlans;
        if (cached is not null && DateTimeOffset.UtcNow - _cachedPlansAt < PlanCacheLifetime)
        {
            return cached;
        }

        await _plansGate.WaitAsync(cancellationToken);
        try
        {
            cached = _cachedPlans;
            if (cached is not null && DateTimeOffset.UtcNow - _cachedPlansAt < PlanCacheLifetime)
            {
                return cached;
            }

            var family = await ResolveProductFamilyAsync(cancellationToken);
            var products = await ListAllFamilyProductsAsync(family.Id!.Value, cancellationToken);
            var plans = products.Select(MapPlan).ToList();

            _cachedPlans = plans;
            _cachedPlansAt = DateTimeOffset.UtcNow;
            return plans;
        }
        finally
        {
            _plansGate.Release();
        }
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(SubscriptionUserInfo user, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException("A plan must be specified.", 400);
        }

        productHandle = productHandle.Trim();

        var plan = await ResolvePlanAsync(productHandle, cancellationToken);
        var customer = await EnsureCustomerAsync(user, cancellationToken);

        var existing = await FindExistingLiveSubscriptionAsync(customer.Id!.Value, productHandle, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "User {UserId} already holds subscription {SubscriptionId} for plan {ProductHandle}; returning it instead of creating a duplicate.",
                user.UserId, existing.Id, productHandle);
            return new SubscriptionEnrollmentResult(MapSummary(existing, plan), AlreadySubscribed: true);
        }

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customer.Id,
                Reference = BuildCorrelationReference(user.UserId, productHandle),
                // The shop's plans do not capture a card at subscribe time; remittance defers
                // payment collection to billing (invoice/check) so no payment profile is needed.
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await CallAsync(
                token => _client.Subscriptions.CreateSubscription(body, token),
                "create subscription", cancellationToken);

            var subscription = response.Subscription;
            if (subscription is null)
            {
                throw new MaxioBillingException("The billing provider returned an empty subscription.", null);
            }

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} for user {UserId} on plan {ProductHandle}.",
                subscription.Id, user.UserId, productHandle);
            return new SubscriptionEnrollmentResult(MapSummary(subscription, plan), AlreadySubscribed: false);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode is null)
        {
            // Transport failure or unparseable response on a write: the create may still have taken
            // effect. Reconcile against provider state before reporting failure.
            var reconciled = await TryReconcileExistingSubscriptionAsync(customer.Id!.Value, productHandle, cancellationToken);
            if (reconciled is not null)
            {
                _logger.LogWarning(
                    "Subscription create for user {UserId} / plan {ProductHandle} failed with an unknown outcome, but a live subscription was found on reconcile.",
                    user.UserId, productHandle);
                return new SubscriptionEnrollmentResult(MapSummary(reconciled, plan), AlreadySubscribed: true);
            }
            throw;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // A 422 can be a genuine validation failure or the double-click race the provider does
            // not dedupe (subscription references are not uniqueness-enforced). Reconcile once.
            var reconciled = await TryReconcileExistingSubscriptionAsync(customer.Id!.Value, productHandle, cancellationToken);
            if (reconciled is not null)
            {
                return new SubscriptionEnrollmentResult(MapSummary(reconciled, plan), AlreadySubscribed: true);
            }

            var messages = new List<string>();
            if (ex.Error.TryGetErrorListResponse1(out var errorList) && errorList.Errors is not null)
            {
                messages.AddRange(errorList.Errors);
            }
            if (messages.Count == 0 && ex.Error.TryGetRawError(out var raw) && raw is not null)
            {
                messages.Add($"HTTP {(int)raw.StatusCode}: {Truncate(raw.ReadAsString(), 500)}");
            }

            var detail = messages.Count > 0
                ? string.Join("; ", messages)
                : "the billing provider rejected the subscription";
            throw new MaxioBillingException($"The subscription could not be created: {detail}", ProviderValidationStatus, ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsForUserAsync(SubscriptionUserInfo user, CancellationToken cancellationToken = default)
    {
        Customer customer;
        try
        {
            customer = await ReadCustomerByReferenceAsync(user.UserId, cancellationToken);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == ProviderNotFoundStatus)
        {
            // No billing profile yet -> no subscriptions. Read-only path must not create one.
            return Array.Empty<SubscriptionSummary>();
        }

        var responses = await ListCustomerSubscriptionsAsync(customer.Id!.Value, cancellationToken);
        return responses
            .Select(r => r.Subscription)
            .Where(s => s is not null)
            .Select(s => MapSummary(s!, null))
            .ToList();
    }

    private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, string operation, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TotalCallBudget);
            return await call(cts.Token);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogError(ex, "Maxio returned a body that could not be parsed during {Operation}.", operation);
            throw new MaxioBillingException("The billing provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Maxio call {Operation} failed at the transport level.", operation);
            throw new MaxioBillingException("The billing provider could not be reached.", null, ex);
        }
    }

    private async Task<ProductFamily> ResolveProductFamilyAsync(CancellationToken cancellationToken)
    {
        var families = await CallAsync(
            token => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: token),
            "list product families", cancellationToken);

        var family = families
            .FirstOrDefault(f => string.Equals(f.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            ?.ProductFamily;

        if (family?.Id is null)
        {
            throw new MaxioBillingException($"Product family '{_settings.ProductFamilyHandle}' was not found in the billing provider.", ProviderNotFoundStatus);
        }

        return family;
    }

    private async Task<IReadOnlyList<Product>> ListAllFamilyProductsAsync(int familyId, CancellationToken cancellationToken)
    {
        const int perPage = 100;
        const int maxPages = 10;
        var products = new List<Product>();
        var page = 1;
        while (page <= maxPages)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await CallAsync(
                    token => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyId.ToString(),
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: null,
                        include: null,
                        page: page,
                        perPage: perPage,
                        ct: token),
                    "list products for product family", cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var message))
                {
                    throw new MaxioBillingException($"The billing provider rejected the plan catalog lookup: {message}", null, ex);
                }
                if (ex.Error.TryGetRawError(out var raw) && raw is not null)
                {
                    throw new MaxioBillingException("The billing provider rejected the plan catalog lookup.", (int)raw.StatusCode, ex);
                }
                throw new MaxioBillingException("The billing provider rejected the plan catalog lookup.", null, ex);
            }

            products.AddRange(batch.Where(r => r.Product is not null).Select(r => r.Product!));
            if (batch.Count < perPage)
            {
                break;
            }
            page++;
        }

        return products;
    }

    private async Task<SubscriptionPlan> ResolvePlanAsync(string productHandle, CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, productHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioBillingException($"Plan '{productHandle}' was not found.", ProviderNotFoundStatus);
        }
        return plan;
    }

    private async Task<Customer> EnsureCustomerAsync(SubscriptionUserInfo user, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadCustomerByReferenceAsync(user.UserId, cancellationToken);
        }
        catch (MaxioBillingException ex) when (ex.StatusCode == ProviderNotFoundStatus)
        {
            return await CreateCustomerAsync(user, cancellationToken);
        }
    }

    private async Task<Customer> ReadCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallAsync(
                token => _client.Customers.ReadCustomerByReference(reference, token),
                "look up customer by reference", cancellationToken);
            return response.Customer!;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == ProviderNotFoundStatus)
        {
            throw new MaxioBillingException($"No billing profile exists for the user yet.", ProviderNotFoundStatus, ex);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio rejected the customer lookup for reference {Reference}.", reference);
            throw new MaxioBillingException("The billing provider rejected the customer lookup.", (int)ex.Error.StatusCode, ex);
        }
    }

    private async Task<Customer> CreateCustomerAsync(SubscriptionUserInfo user, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = user.UserId
            }
        };

        try
        {
            var response = await CallAsync(
                token => _client.Customers.CreateCustomer(body, token),
                "create customer", cancellationToken);

            _logger.LogInformation("Created Maxio customer {CustomerId} (reference {Reference}).", response.Customer?.Id, user.UserId);
            return response.Customer!;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // 422 is expected for the concurrent-create race: customer references are
            // uniqueness-enforced by the provider. Re-read once; only if the customer really
            // is not there is this a genuine rejection.
            var reRead = await TryReReadCustomerAsync(user, cancellationToken);
            if (reRead is not null)
            {
                _logger.LogInformation(
                    "Customer create for reference {Reference} was rejected as duplicate; existing customer {CustomerId} returned.",
                    user.UserId, reRead.Id);
                return reRead;
            }

            var messages = new List<string>();
            if (ex.Error.TryGetCustomerErrorResponse1(out var errorResponse) && errorResponse.Errors is not null)
            {
                if (errorResponse.Errors.PerPage is not null) messages.AddRange(errorResponse.Errors.PerPage);
                if (errorResponse.Errors.PricePoint is not null) messages.AddRange(errorResponse.Errors.PricePoint);
            }
            if (messages.Count == 0 && ex.Error.TryGetRawError(out var raw) && raw is not null)
            {
                messages.Add($"HTTP {(int)raw.StatusCode}: {Truncate(raw.ReadAsString(), 500)}");
            }

            var detail = messages.Count > 0
                ? string.Join("; ", messages)
                : "the billing provider rejected the customer";
            throw new MaxioBillingException($"The billing profile could not be created: {detail}", ProviderValidationStatus, ex);
        }
    }

    private async Task<Customer?> TryReReadCustomerAsync(SubscriptionUserInfo user, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadCustomerByReferenceAsync(user.UserId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer re-read for reference {Reference} after a rejected create did not succeed.", user.UserId);
            return null;
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        try
        {
            return await CallAsync(
                token => _client.Customers.ListCustomerSubscriptions(customerId, token),
                "list customer subscriptions", cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio rejected the subscription listing for customer {CustomerId}.", customerId);
            throw new MaxioBillingException("The billing provider rejected the subscription listing.", (int)ex.Error.StatusCode, ex);
        }
    }

    private async Task<Subscription?> FindExistingLiveSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        var responses = await ListCustomerSubscriptionsAsync(customerId, cancellationToken);
        return responses
            .Select(r => r.Subscription)
            .FirstOrDefault(s => s is not null &&
                                 string.Equals(s.Product?.Handle, productHandle, StringComparison.OrdinalIgnoreCase) &&
                                 IsLiveState(s.State));
    }

    private async Task<Subscription?> TryReconcileExistingSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        try
        {
            return await FindExistingLiveSubscriptionAsync(customerId, productHandle, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reconciliation after a failed subscription create did not succeed for plan {ProductHandle}.", productHandle);
            return null;
        }
    }

    /// <summary>
    /// A subscription counts as "already subscribed" unless the user ended it. Unknown or
    /// missing states are treated as live so a duplicate create is never issued on a blind spot.
    /// </summary>
    private static bool IsLiveState(SubscriptionState? state) =>
        state is null ||
        (state != SubscriptionState.Canceled &&
         state != SubscriptionState.Expired &&
         state != SubscriptionState.FailedToCreate);

    private static string BuildCorrelationReference(string userId, string productHandle)
    {
        var raw = $"{userId}:{productHandle}";
        if (raw.Length <= 40)
        {
            return raw;
        }
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));
        return hash.ToLowerInvariant()[..40];
    }

    private SubscriptionSummary MapSummary(Subscription subscription, SubscriptionPlan? fallbackPlan)
    {
        var product = subscription.Product;
        decimal? price = null;
        if (subscription.ProductPriceInCents is long cents)
        {
            price = cents / 100m;
        }
        else if (product?.PriceInCents is long productCents)
        {
            price = productCents / 100m;
        }
        else if (fallbackPlan is not null)
        {
            price = fallbackPlan.Price;
        }

        return new SubscriptionSummary(
            subscription.Id ?? 0,
            product?.Handle ?? fallbackPlan?.Handle ?? string.Empty,
            product?.Name ?? fallbackPlan?.Name,
            price,
            subscription.State?.Value ?? "unknown",
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt,
            subscription.CurrentPeriodStartedAt,
            subscription.Reference);
    }

    private static SubscriptionPlan MapPlan(Product product) =>
        new(
            product.Handle ?? string.Empty,
            product.Name ?? string.Empty,
            product.Description,
            (product.PriceInCents ?? 0) / 100m,
            product.Interval ?? 1,
            product.IntervalUnit?.Value ?? "month",
            product.RequireCreditCard ?? false);

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
