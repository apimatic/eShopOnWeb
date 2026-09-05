using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int ProductPageSize = 100;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new(StringComparer.Ordinal);
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly CatalogContext _catalogContext;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        CatalogContext catalogContext,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _catalogContext = catalogContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await GetConfiguredProductFamilyAsync(cancellationToken);
        var products = new List<Product>();
        for (var page = 1; ; page++)
        {
            var result = await ExecuteAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: family.Id!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: page,
                perPage: ProductPageSize,
                ct: ct), cancellationToken);

            var currentPage = result.Select(x => x.Product).Where(x => x.ArchivedAt is null).ToList();
            products.AddRange(currentPage);
            if (result.Count < ProductPageSize) break;
        }

        return products
            .Where(x => !string.IsNullOrWhiteSpace(x.Handle))
            .Select(MapPlan)
            .OrderBy(x => x.Price)
            .ToList();
    }

    public async Task<SubscriptionSummary> SubscribeAsync(SubscriptionSubscriber subscriber, string productHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
            throw new SubscriptionBillingException("A subscription plan handle is required.", 400);

        var lockKey = $"{subscriber.UserId}:{productHandle}";
        var gate = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var plans = await GetPlansAsync(cancellationToken);
            var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, productHandle, StringComparison.Ordinal));
            if (plan is null)
                throw new SubscriptionBillingException("The requested subscription plan is not available.", 400);

            var customerReference = CustomerReference(subscriber.UserId);
            var subscriptionReference = SubscriptionReference(subscriber.UserId, plan.Handle);
            var enrollment = await GetOrCreateEnrollmentAsync(subscriber.UserId, plan.Handle, customerReference, subscriptionReference, cancellationToken);

            var customer = await EnsureCustomerAsync(subscriber, customerReference, cancellationToken);
            var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (existing is not null)
                return await CompleteAsync(enrollment, MapSubscription(existing), cancellationToken);

            Subscription? created;
            try
            {
                using var writeScope = MaxioWriteOnceHandler.Begin(subscriptionReference);
                created = (await ExecuteAsync(ct => _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = plan.Handle,
                        CustomerReference = customer.Reference ?? customerReference,
                        Reference = subscriptionReference,
                        PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
                    }
                }, ct: ct), cancellationToken)).Subscription;
            }
            catch (MaxioWriteReplayBlockedException)
            {
                created = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
                if (created is null)
                    throw new SubscriptionBillingException("The subscription outcome is being confirmed. Please check your subscriptions shortly.", 503);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                throw ToProviderException(ex.Error, "The billing provider rejected the subscription.", 422);
            }

            if (created is null)
                throw new SubscriptionBillingException("The billing provider returned an incomplete subscription response.");

            return await CompleteAsync(enrollment, MapSubscription(created), cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsAsync(SubscriptionSubscriber subscriber, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerAsync(CustomerReference(subscriber.UserId), cancellationToken);
        if (customer?.Id is null) return Array.Empty<SubscriptionSummary>();

        var subscriptions = await ExecuteAsync(ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct), cancellationToken);
        return subscriptions
            .Select(x => x.Subscription)
            .Where(x => x is not null)
            .Cast<Subscription>()
            .Select(MapSubscription)
            .ToList();
    }

    private async Task<ProductFamily> GetConfiguredProductFamilyAsync(CancellationToken cancellationToken)
    {
        var families = await ExecuteAsync(ct => _client.ProductFamilies.ListProductFamilies(
            dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct), cancellationToken);
        var family = families.Select(x => x.ProductFamily)
            .FirstOrDefault(x => string.Equals(x?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));
        return family ?? throw new SubscriptionBillingException("The configured subscription catalog is unavailable.");
    }

    private async Task<Customer> EnsureCustomerAsync(SubscriptionSubscriber subscriber, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(customerReference, cancellationToken);
        if (existing is not null) return existing;

        try
        {
            var response = await ExecuteAsync(ct => _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = subscriber.FirstName,
                    LastName = subscriber.LastName,
                    Email = subscriber.Email,
                    Reference = customerReference
                }
            }, ct: ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var recovered = await FindCustomerAsync(customerReference, cancellationToken);
            if (recovered is not null) return recovered;
            throw ToProviderException(ex.Error, "The billing provider rejected the customer.", 422);
        }
    }

    private async Task<Customer?> FindCustomerAsync(string customerReference, CancellationToken cancellationToken)
    {
        try
        {
            return (await ExecuteAsync(ct => _client.Customers.ReadCustomerByReference(customerReference, ct: ct), cancellationToken)).Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex.Error, "The billing provider could not read the customer.");
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string subscriptionReference, CancellationToken cancellationToken)
    {
        try
        {
            return (await ExecuteAsync(ct => _client.Subscriptions.FindSubscription(subscriptionReference, ct: ct), cancellationToken)).Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            throw ToProviderException(ex.Error, "The billing provider could not read the subscription.");
        }
    }

    private async Task<SubscriptionEnrollment> GetOrCreateEnrollmentAsync(string userId, string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken)
    {
        var existing = await _catalogContext.SubscriptionEnrollments.SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        if (existing is not null) return existing;

        var enrollment = new SubscriptionEnrollment
        {
            UserId = userId,
            ProductHandle = productHandle,
            CustomerReference = customerReference,
            SubscriptionReference = subscriptionReference,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _catalogContext.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _catalogContext.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            _catalogContext.Entry(enrollment).State = EntityState.Detached;
            return await _catalogContext.SubscriptionEnrollments.SingleAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        }
    }

    private async Task<SubscriptionSummary> CompleteAsync(SubscriptionEnrollment enrollment, SubscriptionSummary subscription, CancellationToken cancellationToken)
    {
        enrollment.MaxioSubscriptionId = subscription.Id;
        enrollment.CompletedAt = DateTimeOffset.UtcNow;
        await _catalogContext.SaveChangesAsync(cancellationToken);
        return subscription;
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        try
        {
            return await action(budget.Token);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Maxio transport failure");
            throw new SubscriptionBillingException("The billing provider is currently unavailable.", 503, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Maxio request timed out");
            throw new SubscriptionBillingException("The billing provider timed out.", 504, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Maxio returned an unreadable response");
            throw new SubscriptionBillingException("The billing provider returned an unreadable response.", 502, ex);
        }
    }

    private static SubscriptionPlan MapPlan(Product product) => new(
        product.Handle!, product.Name ?? product.Handle!, product.Description,
        (product.PriceInCents ?? 0) / 100m, product.Interval ?? 1, product.IntervalUnit?.Value ?? string.Empty);

    private static SubscriptionSummary MapSubscription(Subscription subscription) => new(
        subscription.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        subscription.Reference ?? string.Empty,
        subscription.Product?.Handle ?? string.Empty,
        subscription.Product?.Name ?? subscription.Product?.Handle ?? string.Empty,
        (subscription.ProductPriceInCents ?? 0) / 100m,
        subscription.State?.Value ?? string.Empty,
        subscription.NextAssessmentAt);

    private static SubscriptionBillingException ToProviderException(RawError error, string message) =>
        new(message, (int)error.StatusCode);

    private static SubscriptionBillingException ToProviderException(CreateCustomerError error, string message, int fallbackStatus)
    {
        if (error.TryGetRawError(out var raw)) return ToProviderException(raw, message);
        return new SubscriptionBillingException(message, fallbackStatus);
    }

    private static SubscriptionBillingException ToProviderException(FindSubscriptionError error, string message)
    {
        if (error.TryGetRawError(out var raw)) return ToProviderException(raw, message);
        return new SubscriptionBillingException(message);
    }

    private SubscriptionBillingException ToProviderException(CreateSubscriptionError error, string message, int fallbackStatus)
    {
        if (error.TryGetErrorListResponse1(out var validationError))
        {
            _logger.LogWarning("Maxio rejected subscription enrollment: {ValidationErrors}", string.Join("; ", validationError.Errors));
            return new SubscriptionBillingException(message, fallbackStatus);
        }
        if (error.TryGetRawError(out var raw)) return ToProviderException(raw, message);
        return new SubscriptionBillingException(message, fallbackStatus);
    }

    private static string CustomerReference(string userId) => $"eshop-customer-{Hash(userId)}";
    private static string SubscriptionReference(string userId, string productHandle) => $"eshop-subscription-{Hash($"{userId}:{productHandle}")}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];
}
