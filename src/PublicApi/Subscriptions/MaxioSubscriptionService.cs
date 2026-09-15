using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> EnrollmentGates = new();
    private readonly MaxioAdvancedBillingClient _client;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _identityDb = identityDb;
        _userManager = userManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var (_, products) = await GetConfiguredFamilyProductsAsync(cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioProviderException("A plan handle is required.", 400);
        }

        var user = await ResolveUserAsync(principal, cancellationToken);
        var normalizedPlanHandle = planHandle.Trim();
        var (_, plans) = await GetConfiguredFamilyProductsAsync(cancellationToken);
        var selectedPlan = plans.SingleOrDefault(p => string.Equals(p.Handle, normalizedPlanHandle, StringComparison.Ordinal));
        if (selectedPlan is null)
        {
            throw new MaxioProviderException("The requested subscription plan is unavailable.", 400);
        }

        var customerReference = ReferenceFor("customer", user.Id);
        var subscriptionReference = ReferenceFor("subscription", $"{user.Id}:{normalizedPlanHandle}");
        var gate = EnrollmentGates.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var enrollment = await GetOrCreateEnrollmentAsync(user.Id, normalizedPlanHandle, customerReference, subscriptionReference, cancellationToken);
            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
            enrollment.MaxioCustomerId = customer.Id;
            await _identityDb.SaveChangesAsync(cancellationToken);

            var subscription = await FindSubscriptionOrNullAsync(subscriptionReference, cancellationToken);
            if (subscription is null)
            {
                subscription = await CreateSubscriptionAndReconcileAsync(customer.Id, normalizedPlanHandle, subscriptionReference, cancellationToken);
            }

            enrollment.MaxioSubscriptionId = subscription.Id;
            enrollment.ConfirmedAtUtc = DateTimeOffset.UtcNow;
            await _identityDb.SaveChangesAsync(cancellationToken);
            return MapSubscription(subscription);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal, cancellationToken);
        var customer = await FindCustomerOrNullAsync(ReferenceFor("customer", user.Id), cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await BoundedAsync(async ct =>
        {
            try
            {
                return await _client.Customers.ListCustomerSubscriptions(
                    customer.Id ?? throw new MaxioProviderException("Maxio returned a customer without an identifier.", 502),
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw ProviderFailure(ex.Error, "Maxio could not retrieve subscriptions.");
            }
        }, cancellationToken);

        return subscriptions
            .Select(x => x.Subscription)
            .Where(x => x is not null)
            .Select(x => MapSubscription(x!))
            .ToList();
    }

    private async Task<(ProductFamily family, IReadOnlyList<Product> products)> GetConfiguredFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var families = await BoundedAsync(async ct =>
        {
            try
            {
                return await _client.ProductFamilies.ListProductFamilies(
                    dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw ProviderFailure(ex.Error, "Maxio could not retrieve subscription plans.");
            }
        }, cancellationToken);

        var family = families
            .Select(x => x.ProductFamily)
            .FirstOrDefault(x => x is not null && string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));
        if (family?.Id is null)
        {
            throw new MaxioProviderException("The configured subscription product family was not found.", 502);
        }

        var products = new List<Product>();
        const int pageSize = 100;
        for (var page = 1; ; page++)
        {
            var response = await BoundedAsync(async ct =>
            {
                try
                {
                    return await _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: family.Id.Value.ToString(), dateField: null, filter: null,
                        startDate: null, endDate: null, startDatetime: null, endDatetime: null,
                        includeArchived: false, include: null, page: page, perPage: pageSize, ct: ct);
                }
                catch (SdkException<ListProductsForProductFamilyError> ex)
                {
                    if (ex.Error.TryGetString(out _))
                    {
                        throw new MaxioProviderException("The configured subscription product family was not found.", 404, ex);
                    }

                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw ProviderFailure(raw, "Maxio could not retrieve subscription plans.", ex);
                    }

                    throw new MaxioProviderException("Maxio rejected the request for subscription plans.", 502, ex);
                }
            }, cancellationToken);

            var usable = response.Select(x => x.Product).Where(x => x is not null && x.ArchivedAt is null).Select(x => x!).ToList();
            products.AddRange(usable);
            if (response.Count < pageSize)
            {
                break;
            }
        }

        return (family, products);
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new MaxioProviderException("The authenticated user could not be resolved.", 401);
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new MaxioProviderException("The authenticated user could not be resolved.", 401);
        }

        return user;
    }

    private async Task<MaxioSubscriptionEnrollment> GetOrCreateEnrollmentAsync(
        string userId, string planHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken)
    {
        var existing = await _identityDb.MaxioSubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.SubscriptionReference == subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var enrollment = new MaxioSubscriptionEnrollment
        {
            UserId = userId,
            PlanHandle = planHandle,
            CustomerReference = customerReference,
            SubscriptionReference = subscriptionReference
        };
        _identityDb.MaxioSubscriptionEnrollments.Add(enrollment);
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            _identityDb.Entry(enrollment).State = EntityState.Detached;
            return await _identityDb.MaxioSubscriptionEnrollments
                .SingleAsync(x => x.SubscriptionReference == subscriptionReference, cancellationToken);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(ApplicationUser user, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerOrNullAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var names = NameFor(user);
        try
        {
            using var writeScope = MaxioWriteRetryGuard.BeginScope();
            return await BoundedAsync(async ct =>
            {
                try
                {
                    var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = names.firstName,
                            LastName = names.lastName,
                            Email = user.Email!,
                            Reference = customerReference
                        }
                    }, ct: ct);
                    return response.Customer ?? throw new MaxioProviderException("Maxio returned an incomplete customer response.", 502);
                }
                catch (SdkException<CreateCustomerError> ex)
                {
                    if (ex.Error.TryGetCustomerErrorResponse1(out _))
                    {
                        throw new MaxioProviderException("Maxio rejected the customer enrollment.", 422, ex);
                    }

                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw ProviderFailure(raw, "Maxio could not create the customer.", ex);
                    }

                    throw new MaxioProviderException("Maxio could not create the customer.", 502, ex);
                }
            }, cancellationToken);
        }
        catch (MaxioWriteRetryPreventedException)
        {
            return await FindCustomerOrNullAsync(customerReference, cancellationToken)
                ?? throw new MaxioProviderException("The customer enrollment outcome could not be confirmed. Please try again.", 503);
        }
        catch (MaxioProviderException ex) when (ex.StatusCode == 422)
        {
            var recovered = await FindCustomerOrNullAsync(customerReference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            throw;
        }
    }

    private async Task<Customer?> FindCustomerOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        return await BoundedAsync(async ct =>
        {
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
                return response.Customer ?? throw new MaxioProviderException("Maxio returned an incomplete customer response.", 502);
            }
            catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
            {
                return null;
            }
            catch (SdkException<RawError> ex)
            {
                throw ProviderFailure(ex.Error, "Maxio could not retrieve the customer.");
            }
        }, cancellationToken);
    }

    private async Task<Subscription> CreateSubscriptionAndReconcileAsync(int? customerId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        if (customerId is null)
        {
            throw new MaxioProviderException("Maxio returned a customer without an identifier.", 502);
        }

        try
        {
            using var writeScope = MaxioWriteRetryGuard.BeginScope();
            return await BoundedAsync(async ct =>
            {
                try
                {
                    var response = await _client.Subscriptions.CreateSubscription(new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = planHandle,
                            CustomerId = customerId,
                            Reference = reference,
                            PaymentCollectionMethod = CollectionMethod.Invoice,
                            NetTerms = "0"
                        }
                    }, ct: ct);
                    return response.Subscription ?? throw new MaxioProviderException("Maxio returned an incomplete subscription response.", 502);
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var errors))
                    {
                        _logger.LogWarning("Maxio rejected subscription enrollment: {Errors}", string.Join("; ", errors.Errors));
                        throw new MaxioProviderException("Maxio rejected the subscription enrollment.", 422, ex);
                    }

                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw ProviderFailure(raw, "Maxio could not create the subscription.", ex);
                    }

                    throw new MaxioProviderException("Maxio could not create the subscription.", 502, ex);
                }
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is MaxioWriteRetryPreventedException || ex is MaxioProviderException)
        {
            var recovered = await FindSubscriptionOrNullAsync(reference, cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }

            if (ex is MaxioProviderException providerException)
            {
                throw providerException;
            }

            throw new MaxioProviderException("The subscription enrollment outcome could not be confirmed. Please try again.", 503, ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        return await BoundedAsync(async ct =>
        {
            try
            {
                var response = await _client.Subscriptions.FindSubscription(reference, ct: ct);
                return response.Subscription;
            }
            catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            catch (SdkException<FindSubscriptionError> ex)
            {
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw ProviderFailure(raw, "Maxio could not retrieve the subscription.", ex);
                }

                throw new MaxioProviderException("Maxio could not retrieve the subscription.", 502, ex);
            }
        }, cancellationToken);
    }

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            return await operation(deadline.Token);
        }
        catch (MaxioProviderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Maxio request failed before a response was available.");
            throw new MaxioProviderException("The billing provider is temporarily unavailable.", 502, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Maxio returned a response that could not be processed.");
            throw new MaxioProviderException("The billing provider returned an invalid response.", 502, ex);
        }
    }

    private static SubscriptionPlanDto MapPlan(Product product) => new(
        product.Handle ?? string.Empty, product.Name ?? string.Empty, product.Description,
        ToInt32OrNull(product.PriceInCents), product.Interval, product.IntervalUnit?.Value);

    private static SubscriptionDto MapSubscription(Subscription subscription) => new(
        subscription.Id, subscription.Reference, subscription.Product?.Handle, subscription.Product?.Name,
        ToInt32OrNull(subscription.CurrentBillingAmountInCents ?? subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents),
        subscription.State?.Value, subscription.CurrentPeriodEndsAt, subscription.NextAssessmentAt);

    private static int? ToInt32OrNull(long? value)
        => value is null ? null : checked((int)value.Value);

    private static MaxioProviderException ProviderFailure(RawError error, string message, Exception? innerException = null)
        => new(message, (int)error.StatusCode, innerException);

    private static string ReferenceFor(string type, string value)
        => $"eshop-{type}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    private static (string firstName, string lastName) NameFor(ApplicationUser user)
    {
        var localPart = user.Email!.Split('@', 2)[0].Replace('.', ' ');
        var parts = localPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("eShop", "Shopper"),
            1 => (parts[0], "Shopper"),
            _ => (parts[0], parts[^1])
        };
    }
}
