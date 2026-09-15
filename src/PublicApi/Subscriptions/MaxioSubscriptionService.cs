using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// The only layer that knows Maxio SDK types. Public endpoint DTOs never expose provider models.
/// </summary>
public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const int ProductsPerPage = 100;
    private const int MaximumProductPages = 10;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppIdentityDbContext _identityContext;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioSettings settings,
        UserManager<ApplicationUser> userManager,
        AppIdentityDbContext identityContext,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _settings = settings;
        _userManager = userManager;
        _identityContext = identityContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var products = await ListFamilyProductsAsync(cancellationToken);
            return products
                .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle) && product.Id.HasValue)
                .Select(product => new SubscriptionPlanDto(
                    product.Handle!,
                    product.Name ?? product.Handle!,
                    product.Description,
                    product.PriceInCents,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    null))
                .ToList();
        }
        catch (SdkException<RawError> exception)
        {
            throw ProviderFailure("Maxio could not load subscription plans.", exception.Error.StatusCode, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("Maxio could not load subscription plans.", null, exception);
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionRequestException("A product handle is required.");
        }

        var user = await GetUserAsync(username);
        var normalizedHandle = productHandle.Trim();
        var lockKey = $"{user.Id}:{normalizedHandle}";
        var gate = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            var product = await ResolveProductAsync(normalizedHandle, cancellationToken);
            var customer = await ResolveCustomerAsync(user, cancellationToken);
            var subscriptionReference = SubscriptionReference(user.Id, product.Handle!);

            var enrollment = await _identityContext.SubscriptionEnrollments
                .SingleOrDefaultAsync(item => item.UserId == user.Id && item.ProductHandle == product.Handle!, cancellationToken);

            var existing = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
            SubscriptionResponse subscriptionResponse;
            if (existing is not null)
            {
                subscriptionResponse = existing;
            }
            else
            {
                try
                {
                    subscriptionResponse = await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(
                        new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = product.Handle,
                                CustomerId = customer.Id,
                                Reference = subscriptionReference,
                                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
                            }
                        }, ct), cancellationToken);
                }
                catch (SdkException<CreateSubscriptionError> exception) when (exception.Error.TryGetErrorListResponse1(out var errorResponse))
                {
                    // A simultaneous request may have completed first. Reconcile by the deterministic reference.
                    existing = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
                    if (existing is null)
                    {
                        _logger.LogWarning(
                            exception,
                            "Maxio rejected subscription enrollment for product handle {ProductHandle}: {ProviderErrors}",
                            product.Handle,
                            string.Join("; ", errorResponse.Errors));
                        throw ProviderFailure("Maxio rejected the subscription request.", HttpStatusCode.UnprocessableEntity, exception);
                    }

                    subscriptionResponse = existing;
                }
                catch (SdkException<CreateSubscriptionError> exception)
                {
                    throw ProviderFailure("Maxio rejected the subscription request.", HttpStatusCode.BadRequest, exception);
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
                {
                    existing = await TryFindSubscriptionAsync(subscriptionReference, cancellationToken);
                    if (existing is null)
                    {
                        throw ProviderFailure("The Maxio subscription outcome could not be confirmed.", null, exception);
                    }

                    subscriptionResponse = existing;
                }
            }

            var subscriptionId = subscriptionResponse.Subscription?.Id
                ?? throw new MaxioProviderException("Maxio returned a subscription without an identifier.");
            var confirmed = await ReadSubscriptionAsync(subscriptionId, cancellationToken);

            if (enrollment is null)
            {
                enrollment = new SubscriptionEnrollment
                {
                    UserId = user.Id,
                    ProductHandle = product.Handle!,
                    MaxioCustomerReference = CustomerReference(user.Id),
                    MaxioSubscriptionReference = subscriptionReference,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _identityContext.SubscriptionEnrollments.Add(enrollment);
            }

            enrollment.MaxioCustomerId = customer.Id;
            enrollment.MaxioSubscriptionId = confirmed.Id;
            enrollment.ConfirmedAt = DateTimeOffset.UtcNow;
            await _identityContext.SaveChangesAsync(cancellationToken);
            return ToDto(confirmed);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(username);
        var customer = await ResolveCustomerAsync(user, cancellationToken);

        try
        {
            var subscriptions = await BoundedAsync(ct => _client.Customers.ListCustomerSubscriptions(customer.Id!.Value, ct), cancellationToken);
            return subscriptions
                .Where(item => item.Subscription?.Id is not null)
                .Select(item => ToDto(item.Subscription!))
                .ToList();
        }
        catch (SdkException<RawError> exception)
        {
            throw ProviderFailure("Maxio could not load subscriptions.", exception.Error.StatusCode, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("Maxio could not load subscriptions.", null, exception);
        }
    }

    private async Task<ApplicationUser> GetUserAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new SubscriptionRequestException("An authenticated user is required.");
        }

        var user = await _userManager.FindByNameAsync(username);
        return user ?? throw new SubscriptionRequestException("The authenticated user no longer exists.");
    }

    private async Task<Product> ResolveProductAsync(string productHandle, CancellationToken cancellationToken)
    {
        var product = (await ListFamilyProductsAsync(cancellationToken))
            .SingleOrDefault(item => item.ArchivedAt is null && string.Equals(item.Handle, productHandle, StringComparison.Ordinal));
        return product ?? throw new SubscriptionRequestException("The requested subscription plan is not available.");
    }

    private async Task<IReadOnlyList<Product>> ListFamilyProductsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var families = await BoundedAsync(ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct), cancellationToken);
            var matchingFamilies = families
                .Select(response => response.ProductFamily)
                .Where(family => family is not null && string.Equals(family.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal))
                .ToList();

            if (matchingFamilies.Count != 1 || !matchingFamilies[0]!.Id.HasValue)
            {
                throw new MaxioProviderException("The configured Maxio product family could not be resolved.");
            }

            var products = new List<Product>();
            for (var page = 1; page <= MaximumProductPages; page++)
            {
                var current = await BoundedAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                    matchingFamilies[0]!.Id!.Value.ToString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: true,
                    include: null,
                    page: page,
                    perPage: ProductsPerPage,
                    ct: ct), cancellationToken);
                products.AddRange(current.Select(response => response.Product));
                if (current.Count < ProductsPerPage)
                {
                    return products;
                }
            }

            throw new MaxioProviderException("The configured Maxio product family exceeds the supported catalog size.");
        }
        catch (SdkException<RawError> exception)
        {
            throw ProviderFailure("The configured Maxio product family could not be loaded.", exception.Error.StatusCode, exception);
        }
        catch (SdkException<ListProductsForProductFamilyError> exception)
        {
            throw ProviderFailure("The configured Maxio product family could not be loaded.", HttpStatusCode.BadRequest, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("The configured Maxio product family could not be loaded.", null, exception);
        }
    }

    private async Task<Customer> ResolveCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existing = await TryReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new SubscriptionRequestException("The authenticated user needs an email address before subscribing.");
        }

        var firstName = user.UserName?.Split('@')[0];
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = "eShop";
        }

        try
        {
            var created = await BoundedAsync(ct => _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = "Customer",
                        Email = email,
                        Reference = reference
                    }
                }, ct), cancellationToken);
            return created.Customer ?? throw new MaxioProviderException("Maxio returned a customer without details.");
        }
        catch (SdkException<CreateCustomerError> exception) when (exception.Error.TryGetCustomerErrorResponse1(out _))
        {
            existing = await TryReadCustomerAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw ProviderFailure("Maxio rejected the customer enrollment request.", HttpStatusCode.UnprocessableEntity, exception);
        }
        catch (SdkException<CreateCustomerError> exception)
        {
            throw ProviderFailure("Maxio rejected the customer enrollment request.", HttpStatusCode.BadRequest, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            existing = await TryReadCustomerAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw ProviderFailure("The Maxio customer outcome could not be confirmed.", null, exception);
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(reference, ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw ProviderFailure("Maxio could not resolve the customer.", exception.Error.StatusCode, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("Maxio could not resolve the customer.", null, exception);
        }
    }

    private async Task<SubscriptionResponse?> TryFindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedAsync(ct => _client.Subscriptions.FindSubscription(reference, ct), cancellationToken);
        }
        catch (SdkException<FindSubscriptionError> exception) when (exception.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (SdkException<FindSubscriptionError> exception)
        {
            throw ProviderFailure("Maxio could not resolve the subscription.", HttpStatusCode.BadRequest, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("Maxio could not resolve the subscription.", null, exception);
        }
    }

    private async Task<Subscription> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: ct), cancellationToken);
            return response.Subscription ?? throw new MaxioProviderException("Maxio returned a subscription without details.");
        }
        catch (SdkException<RawError> exception)
        {
            throw ProviderFailure("Maxio could not confirm the subscription.", exception.Error.StatusCode, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw ProviderFailure("Maxio could not confirm the subscription.", null, exception);
        }
    }

    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        return await operation(deadline.Token);
    }

    private MaxioProviderException ProviderFailure(string message, HttpStatusCode? providerStatus, Exception exception)
    {
        _logger.LogWarning(exception, "Maxio subscription operation failed with provider status {ProviderStatus}.", providerStatus);
        return new MaxioProviderException(message, providerStatus, exception);
    }

    private static SubscriptionDto ToDto(Subscription subscription) => new(
        subscription.Id ?? throw new MaxioProviderException("Maxio returned a subscription without an identifier."),
        subscription.Product?.Handle,
        subscription.Product?.Name,
        subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? subscription.CurrentBillingAmountInCents,
        subscription.Currency,
        subscription.State?.Value,
        subscription.NextAssessmentAt);

    private static string CustomerReference(string userId) => $"eshop-customer-{Hash(userId)}";
    private static string SubscriptionReference(string userId, string productHandle) => $"eshop-subscription-{Hash($"{userId}:{productHandle}")}";

    private static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
