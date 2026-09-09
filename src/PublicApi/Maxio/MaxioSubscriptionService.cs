using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maxio Advanced Billing-backed implementation. Every SDK call is wrapped in a guard
/// that bounds the whole call and converts SDK failures into <see cref="MaxioBillingException"/>,
/// so no SDK type or exception leaks past this boundary.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxCatalogPages = 20;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    private int? _productFamilyId;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<MaxioPlanInfo>> GetPlansAsync(CancellationToken ct)
    {
        EnsureConfigured();
        return GuardAsync(async token =>
        {
            var familyId = await ResolveProductFamilyIdAsync(token);

            var plans = new List<MaxioPlanInfo>();
            const int perPage = 50;
            var page = 1;
            while (true)
            {
                var products = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
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
                    ct: token);

                plans.AddRange(products
                    .Select(p => p.Product)
                    .Where(p => p is not null && !string.IsNullOrEmpty(p.Handle))
                    .Select(p => MapPlan(p!)));

                if (products.Count < perPage || page >= MaxCatalogPages)
                {
                    break;
                }
                page++;
            }

            return (IReadOnlyList<MaxioPlanInfo>)plans;
        }, ct);
    }

    public Task<MaxioSubscriptionInfo> SubscribeAsync(MaxioSubscriber subscriber, string planHandle, CancellationToken ct)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioBillingException("A plan handle is required to subscribe.", 400);
        }

        return GuardAsync(async token =>
        {
            var product = await ResolveProductByHandleAsync(planHandle, token);
            var customer = await EnsureCustomerAsync(subscriber, token);

            var reference = BuildSubscriptionReference(subscriber.UserId, planHandle);
            var existing = await FindSubscriptionAsync(reference, token);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {UserId} is already subscribed to plan {PlanHandle} (subscription {SubscriptionId})",
                    subscriber.UserId, planHandle, existing.Id);
                return MapSubscription(existing);
            }

            try
            {
                var response = await _client.Subscriptions.CreateSubscription(
                    new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = product.Handle,
                            CustomerId = customer.Id,
                            Reference = reference,
                            // Cardless signup: remittance collection does not require a payment
                            // profile on file, unlike automatic collection.
                            PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
                        }
                    }, token);

                _logger.LogInformation(
                    "User {UserId} subscribed to plan {PlanHandle} as Maxio subscription {SubscriptionId} ({State})",
                    subscriber.UserId, planHandle, response.Subscription?.Id, response.Subscription?.State?.Value);

                return MapSubscription(response.Subscription);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // A concurrent double-click may have created the subscription between our
                // lookup and this create; a duplicate-reference 422 means it now exists.
                if (ex.Error.TryGetErrorListResponse1(out var errors))
                {
                    var raced = await FindSubscriptionAsync(reference, token);
                    if (raced is not null)
                    {
                        _logger.LogInformation(
                            "Concurrent subscribe for user {UserId}/plan {PlanHandle} resolved to existing subscription {SubscriptionId}",
                            subscriber.UserId, planHandle, raced.Id);
                        return MapSubscription(raced);
                    }

                    _logger.LogWarning("Maxio rejected the subscription create: {Errors}", string.Join("; ", errors.Errors));
                    throw new MaxioBillingException(
                        $"The billing provider rejected the subscription: {string.Join("; ", errors.Errors)}.", 422, ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new MaxioBillingException(
                        $"The billing provider rejected the subscription (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode, ex);
                }

                throw new MaxioBillingException("The billing provider rejected the subscription.", 502, ex);
            }
        }, ct);
    }

    public Task<IReadOnlyList<MaxioSubscriptionInfo>> GetSubscriptionsForUserAsync(string userId, CancellationToken ct)
    {
        EnsureConfigured();
        return GuardAsync(async token =>
        {
            var customer = await FindCustomerByReferenceAsync(userId, token);
            if (customer?.Id is null)
            {
                return (IReadOnlyList<MaxioSubscriptionInfo>)Array.Empty<MaxioSubscriptionInfo>();
            }

            var responses = await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, token);
            return (IReadOnlyList<MaxioSubscriptionInfo>)responses
                .Select(r => MapSubscription(r.Subscription))
                .ToList();
        }, ct);
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken token)
    {
        var cached = _productFamilyId;
        if (cached.HasValue)
        {
            return cached.Value;
        }

        var families = await _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: token);

        var family = families.FirstOrDefault(f =>
            string.Equals(f.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.ProductFamily?.Id is not int familyId)
        {
            throw new MaxioBillingException(
                $"No product family with handle '{_options.ProductFamilyHandle}' exists at the billing provider.", 500);
        }

        _productFamilyId = familyId;
        return familyId;
    }

    private async Task<Product> ResolveProductByHandleAsync(string handle, CancellationToken token)
    {
        Product product;
        try
        {
            product = (await _client.Products.ReadProductByHandle(handle, token)).Product
                ?? throw new MaxioBillingException($"Subscription plan '{handle}' was not found.", 404);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioBillingException($"Subscription plan '{handle}' was not found.", 404, ex);
        }

        var familyHandle = product.ProductFamily?.Handle;
        if (!string.IsNullOrEmpty(familyHandle) &&
            !string.Equals(familyHandle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
        {
            throw new MaxioBillingException($"Plan '{handle}' is not part of the subscription catalog.", 400);
        }

        return product;
    }

    private async Task<Customer> EnsureCustomerAsync(MaxioSubscriber subscriber, CancellationToken token)
    {
        var existing = await FindCustomerByReferenceAsync(subscriber.UserId, token);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitName(subscriber);
        try
        {
            var response = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = subscriber.Email,
                        Reference = subscriber.UserId
                    }
                }, token);

            return response.Customer
                ?? throw new MaxioBillingException("The billing provider returned no customer after creation.", 502);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A concurrent double-click may have created the customer in the meantime.
            var raced = await FindCustomerByReferenceAsync(subscriber.UserId, token);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Concurrent customer create for user {UserId} resolved to existing customer {CustomerId}",
                    subscriber.UserId, raced.Id);
                return raced;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                _logger.LogWarning("Maxio rejected the customer create for user {UserId}", subscriber.UserId);
                throw new MaxioBillingException("The billing provider rejected the customer record.", 422, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioBillingException(
                    $"The billing provider rejected the customer record (HTTP {(int)raw.StatusCode}).", (int)raw.StatusCode, ex);
            }

            throw new MaxioBillingException("The billing provider rejected the customer record.", 502, ex);
        }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            return (await _client.Customers.ReadCustomerByReference(reference, token)).Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken token)
    {
        try
        {
            return (await _client.Subscriptions.FindSubscription(reference, token)).Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            // 404 is "no subscription with this reference"; the body shape of that 404 is
            // provider-dependent, so treat both the no-content and raw-body variants as a miss.
            if (ex.Error.TryGetNoContent(out var noContent) && IsNotFound(noContent))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw) && IsNotFound(raw))
            {
                return null;
            }

            _logger.LogWarning(ex, "Failed to look up a Maxio subscription by reference {Reference}", reference);
            throw new MaxioBillingException(
                "The billing provider could not look up the subscription.", 502, ex);
        }
    }

    private static bool IsNotFound(RawError error) =>
        error.StatusCode == HttpStatusCode.NotFound;

    private static MaxioPlanInfo MapPlan(Product product) =>
        new(
            product.Handle!,
            product.Name ?? product.Handle!,
            product.Description,
            (product.PriceInCents ?? 0) / 100m,
            product.PriceInCents ?? 0,
            DescribeInterval(product.Interval, product.IntervalUnit),
            product.RequestCreditCard ?? product.RequireCreditCard ?? false);

    private static MaxioSubscriptionInfo MapSubscription(Subscription? subscription)
    {
        if (subscription is null)
        {
            throw new MaxioBillingException("The billing provider returned no subscription.", 502);
        }

        var priceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0;
        return new MaxioSubscriptionInfo(
            subscription.Id ?? 0,
            subscription.State?.Value ?? "unknown",
            subscription.Product?.Handle ?? string.Empty,
            subscription.Product?.Name ?? string.Empty,
            priceInCents / 100m,
            priceInCents,
            subscription.CurrentPeriodStartedAt,
            subscription.CurrentPeriodEndsAt,
            subscription.ActivatedAt,
            subscription.Currency);
    }

    private static string BuildSubscriptionReference(string userId, string planHandle) =>
        $"{userId}:{planHandle}";

    private static string DescribeInterval(int? interval, IntervalUnit? unit)
    {
        var count = interval ?? 1;
        var unitName = string.Equals(unit?.Value, "day", StringComparison.OrdinalIgnoreCase) ? "day" : "month";
        return count == 1 ? $"per {unitName}" : $"every {count} {unitName}s";
    }

    private static (string FirstName, string LastName) SplitName(MaxioSubscriber subscriber)
    {
        var localPart = subscriber.Email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = parts.Length > 0 && parts[0].Length > 0 ? Capitalize(parts[0]) : "eShop";
        var lastName = parts.Length > 1 && parts[1].Length > 0 ? Capitalize(parts[1]) : "Customer";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        char.ToUpperInvariant(value[0]) + value[1..];

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioBillingException(
                "Maxio billing is not configured: Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle must all be set.", 500);
        }
    }

    /// <summary>
    /// Bounds every SDK call with a total budget and converts every remaining failure
    /// into <see cref="MaxioBillingException"/> with a caller-safe message.
    /// </summary>
    private async Task<T> GuardAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogError(ex, "Maxio API error: HTTP {StatusCode}", (int)ex.Error.StatusCode);
            throw new MaxioBillingException(
                $"The billing provider returned HTTP {(int)ex.Error.StatusCode}.", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "A Maxio response body could not be parsed");
            throw new MaxioBillingException(
                "The billing provider returned a response that could not be processed.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogError(ex, "The Maxio API was unreachable or timed out");
            throw new MaxioBillingException("The billing provider did not respond in time.", 503, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while calling the Maxio API");
            throw new MaxioBillingException("An unexpected billing error occurred.", 502, ex);
        }
    }
}
