using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Recurring-subscription billing via Maxio Advanced Billing. The eShopOnWeb
/// username (the JWT name claim, an email in the seeded data) is the stable
/// Maxio customer <c>reference</c>, making the customer-ensure and the
/// subscribe flows idempotent across double-clicks and application restarts.
/// Maxio is the system of record — no subscription state is persisted locally.
/// </summary>
public class MaxioSubscriptionService : ISubscriptionService
{
    private const int ProductsPageSize = 50;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FamilyCacheLifetime = TimeSpan.FromMinutes(10);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly IAppLogger<MaxioSubscriptionService> _logger;

    // Serializes the find-or-create sequence per (user, plan) so concurrent
    // double-clicks cannot both pass the pre-check and create twice.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscribeLocks = new();

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        IMemoryCache cache,
        IAppLogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken)
    {
        return await GuardAsync(async ct =>
        {
            var familyId = await GetFamilyIdAsync(ct);

            var plans = new List<SubscriptionPlanInfo>();
            var page = 1;
            while (true)
            {
                IReadOnlyList<ProductResponse> products;
                try
                {
                    products = await _client.ProductFamilies.ListProductsForProductFamily(
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
                        perPage: ProductsPageSize,
                        ct: ct);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw Translate(raw, $"listing plans in product family '{_options.ProductFamilyHandle}'");
                    }
                    throw;
                }

                foreach (var response in products)
                {
                    var product = response.Product;
                    if (product?.Handle is null)
                    {
                        continue;
                    }
                    plans.Add(new SubscriptionPlanInfo
                    {
                        Handle = product.Handle,
                        Name = product.Name ?? product.Handle,
                        Description = product.Description,
                        PriceInCents = product.PriceInCents ?? 0,
                        Interval = product.Interval ?? 1,
                        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty
                    });
                }

                if (products.Count < ProductsPageSize)
                {
                    return plans;
                }
                page++;
            }
        }, cancellationToken);
    }

    public async Task<SubscriptionInfo> SubscribeAsync(string username, string planHandle, CancellationToken cancellationToken)
    {
        return await GuardAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new MaxioBillingException("An authenticated user is required to subscribe.", 401);
            }
            if (string.IsNullOrWhiteSpace(planHandle))
            {
                throw new MaxioBillingException("A subscription plan handle is required.", 400);
            }

            await ValidatePlanHandleAsync(planHandle, ct);
            var customer = await EnsureCustomerAsync(username, ct);

            var reference = SubscriptionReference(username, planHandle);
            var subscriptionLock = _subscribeLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
            await subscriptionLock.WaitAsync(ct);
            try
            {
                var existing = await FindSubscriptionOrNullAsync(reference, ct);
                if (existing?.Subscription is not null &&
                    !IsTerminated(existing.Subscription.State))
                {
                    return MapSubscription(existing.Subscription);
                }

                // A terminated (canceled/expired) subscription still occupies
                // the reference, so a re-subscribe gets a fresh, unique one.
                var effectiveReference = existing is null
                    ? reference
                    : $"{reference}:{DateTime.UtcNow:yyyyMMddHHmmssfff}";

                var body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = planHandle,
                        CustomerId = customer.Id!.Value,
                        Reference = effectiveReference,
                        // The storefront does not capture a card, and automatic
                        // collection is rejected without one. Remittance bills
                        // the customer outside the gateway, which is what a
                        // cardless signup on these plans means.
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                };

                SubscriptionResponse response;
                try
                {
                    response = await _client.Subscriptions.CreateSubscription(body, ct);
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var errors) && errors?.Errors is { Count: > 0 } messages)
                    {
                        if (messages.Any(m => m.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                                              m.Contains("already", StringComparison.OrdinalIgnoreCase)))
                        {
                            // Lost a race with another instance — read what
                            // actually exists instead of failing.
                            var raced = await FindSubscriptionOrNullAsync(effectiveReference, ct);
                            if (raced?.Subscription is not null)
                            {
                                return MapSubscription(raced.Subscription);
                            }
                        }
                        throw new MaxioBillingException(
                            $"The billing provider rejected the subscription: {string.Join("; ", messages)}", 422);
                    }
                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw Translate(raw, "creating the subscription");
                    }
                    throw;
                }

                if (response.Subscription is null)
                {
                    throw new MaxioBillingException(
                        "The billing provider returned no subscription after enrollment.");
                }
                return MapSubscription(response.Subscription);
            }
            finally
            {
                subscriptionLock.Release();
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsForUserAsync(string username, CancellationToken cancellationToken)
    {
        return await GuardAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new MaxioBillingException("An authenticated user is required.", 401);
            }

            var customer = await ReadCustomerByReferenceOrNullAsync(username, ct);
            if (customer is null)
            {
                return new List<SubscriptionInfo>();
            }

            IReadOnlyList<SubscriptionResponse> responses;
            try
            {
                responses = await _client.Customers.ListCustomerSubscriptions(customer.Id!.Value, ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw Translate(ex.Error, $"listing subscriptions for customer '{username}'");
            }

            return responses
                .Where(r => r.Subscription is not null)
                .Select(r => MapSubscription(r.Subscription!))
                .ToList();
        }, cancellationToken);
    }

    private async Task ValidatePlanHandleAsync(string planHandle, CancellationToken ct)
    {
        Product product;
        try
        {
            var response = await _client.Products.ReadProductByHandle(planHandle, ct);
            product = response.Product;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioBillingException($"The subscription plan '{planHandle}' was not found.", 404);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, $"reading plan '{planHandle}'");
        }

        if (product is null || product.ProductFamily?.Handle != _options.ProductFamilyHandle)
        {
            throw new MaxioBillingException($"The subscription plan '{planHandle}' is not available.", 404);
        }
    }

    /// <summary>
    /// Idempotently ensures a Maxio customer exists for the eShopOnWeb user:
    /// look up by reference, create on a miss, and on a 422 (or an error body
    /// that fails to parse — likely the duplicate-reference rejection, whose
    /// exact payload is not modeled by the SDK) re-look-up and use whatever
    /// exists. Never creates two customers for the same reference.
    /// </summary>
    private async Task<Customer> EnsureCustomerAsync(string username, CancellationToken ct)
    {
        var existing = await ReadCustomerByReferenceOrNullAsync(username, ct);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = FirstName(username),
                    LastName = LastName(username),
                    Email = username,
                    Reference = username
                }
            }, ct);
            return response.Customer!;
        }
        catch (SdkException<CreateCustomerError>)
        {
            var raced = await ReadCustomerByReferenceOrNullAsync(username, ct);
            if (raced is not null)
            {
                _logger.LogInformation(
                    $"Customer creation for '{username}' was rejected; an existing customer was used instead.");
                return raced;
            }
            throw new MaxioBillingException(
                "The billing provider rejected creating the customer.", 422);
        }
        catch (JsonException)
        {
            // The error body did not match the modeled shape. Treat it as a
            // rejection and settle the outcome by re-reading provider state.
            var raced = await ReadCustomerByReferenceOrNullAsync(username, ct);
            if (raced is not null)
            {
                return raced;
            }
            throw new MaxioBillingException(
                "The billing provider returned a response that could not be processed while creating the customer.");
        }
    }

    private async Task<Customer?> ReadCustomerByReferenceOrNullAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, $"looking up customer '{reference}'");
        }
    }

    private async Task<SubscriptionResponse?> FindSubscriptionOrNullAsync(string reference, CancellationToken ct)
    {
        try
        {
            return await _client.Subscriptions.FindSubscription(reference, ct);
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            // The miss is signalled by 404 — either as the status-specific
            // accessor or via the raw fallback.
            if (ex.Error.TryGetNoContent(out var noContent) && noContent.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out raw))
            {
                throw Translate(raw, $"looking up subscription '{reference}'");
            }
            throw;
        }
    }

    private async Task<int> GetFamilyIdAsync(CancellationToken ct)
    {
        var familyId = await _cache.GetOrCreateAsync<int?>(CacheKey(), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = FamilyCacheLifetime;

            IReadOnlyList<ProductFamilyResponse> families;
            try
            {
                families = await _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw Translate(ex.Error, $"listing product families (configured handle '{_options.ProductFamilyHandle}')");
            }

            var family = families.FirstOrDefault(f =>
                string.Equals(f.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
            if (family?.ProductFamily?.Id is null)
            {
                throw new InvalidOperationException(
                    $"The configured billing product family '{_options.ProductFamilyHandle}' was not found in the billing system.");
            }
            return family.ProductFamily.Id.Value;
        });
        if (familyId is null)
        {
            throw new InvalidOperationException(
                $"The configured billing product family '{_options.ProductFamilyHandle}' was not found in the billing system.");
        }

        return familyId.Value;
    }

    private static SubscriptionInfo MapSubscription(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id ?? 0,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        ProductPriceInCents = subscription.ProductPriceInCents,
        CurrentBillingAmountInCents = subscription.CurrentBillingAmountInCents,
        State = subscription.State?.Value ?? string.Empty,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt
    };

    private static bool IsTerminated(SubscriptionState? state) =>
        state == SubscriptionState.Canceled || state == SubscriptionState.Expired;

    private static string SubscriptionReference(string username, string planHandle) =>
        $"eshopweb:{username}:{planHandle}";

    private string CacheKey() => $"maxio:family-id:{_options.ProductFamilyHandle}";

    private static MaxioBillingException Translate(RawError raw, string what) =>
        new($"The billing provider returned HTTP {(int)raw.StatusCode} while {what}.", (int)raw.StatusCode);

    /// <summary>
    /// One error boundary for every Maxio call: typed SDK errors are already
    /// translated to <see cref="MaxioBillingException"/> inside, so this only
    /// needs to catch unparseable provider bodies and transport failures,
    /// and to bound the whole operation's duration.
    /// </summary>
    private async Task<T> GuardAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        try
        {
            return await operation(budget.Token);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            // A 2xx body that no longer matches its model. The outcome is
            // genuinely unknown — surface infrastructure failure, not absence.
            throw new MaxioBillingException(
                "The billing provider returned a response that could not be processed.");
        }
        catch (TaskCanceledException)
        {
            throw new MaxioBillingException(
                "The billing provider did not respond in time.");
        }
        catch (HttpRequestException)
        {
            throw new MaxioBillingException(
                "The billing provider could not be reached.");
        }
    }

    private static string FirstName(string email)
    {
        var local = LocalPart(email);
        var dot = local.IndexOf('.');
        return dot > 0 ? local[..dot] : local;
    }

    private static string LastName(string email)
    {
        var local = LocalPart(email);
        var dot = local.IndexOf('.');
        return dot > 0 ? local[(dot + 1)..] : LocalPart(email) + " (eShopOnWeb)";
    }

    private static string LocalPart(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return string.IsNullOrWhiteSpace(local) ? "customer" : local;
    }
}
