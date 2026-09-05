using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService
{
    private const int ProductPageSize = 100;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly AppIdentityDbContext _identityDb;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        AppIdentityDbContext identityDb,
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager,
        IOptions<MaxioOptions> options)
    {
        _client = client;
        _identityDb = identityDb;
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await LoadFamilyProductsAsync(cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => new SubscriptionPlanDto
            {
                Handle = product.Handle!,
                Name = product.Name ?? product.Handle!,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit?.Value,
                ProductPricePointHandle = product.ProductPricePointHandle,
                RequiresPaymentMethod = product.RequireCreditCard ?? false
            })
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string productHandle, CancellationToken cancellationToken)
    {
        var identity = GetIdentity();
        var applicationUser = await _userManager.FindByNameAsync(identity)
            ?? throw new MaxioSubscriptionException(401, "The authenticated user could not be found.");
        var references = CreateReferences(applicationUser.Id, identity);
        var products = await LoadFamilyProductsAsync(cancellationToken);
        var product = products.FirstOrDefault(candidate =>
            string.Equals(candidate.Handle, productHandle?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            candidate.ArchivedAt is null);

        if (product is null || string.IsNullOrWhiteSpace(product.Handle))
        {
            throw new MaxioSubscriptionException(404, "The requested subscription plan was not found.");
        }

        if (product.RequireCreditCard == true)
        {
            throw new MaxioSubscriptionException(422, "This subscription plan requires a payment method, which is not supported by this flow.");
        }

        var userLock = UserLocks.GetOrAdd(references.UserReference, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var mapping = await _identityDb.MaxioSubscriptionMappings
                .SingleOrDefaultAsync(item => item.ApplicationUserId == references.ApplicationUserId, cancellationToken);

            if (mapping is not null)
            {
                if (!string.Equals(mapping.ProductHandle, product.Handle, StringComparison.OrdinalIgnoreCase))
                {
                    throw new MaxioSubscriptionException(409, "The account already has a different active subscription.");
                }

                var mappedSubscription = await ReadSubscriptionAsync(mapping.MaxioSubscriptionId, cancellationToken);
                return MapSubscription(mappedSubscription, product);
            }

            var customerId = await EnsureCustomerAsync(references.CustomerReference, identity, cancellationToken);
            var existingSubscription = await FindSubscriptionAsync(references.SubscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                var existing = await SaveMappingAndReturnAsync(
                    references, customerId, existingSubscription, product, cancellationToken);
                return existing;
            }

            var request = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = product.Handle,
                    ProductPricePointHandle = product.ProductPricePointHandle,
                    CustomerId = customerId,
                    Reference = references.SubscriptionReference,
                    PaymentCollectionMethod = CollectionMethod.Invoice
                }
            };

            Subscription created;
            try
            {
                using (MaxioWriteOnceHandler.Begin())
                {
                    var response = await _client.Subscriptions.CreateSubscription(request, ct: cancellationToken);
                    created = response.Subscription
                        ?? throw new MaxioSubscriptionException(502, "Maxio returned an empty subscription response.");
                }
            }
            catch (MaxioWriteAlreadySentException exception)
            {
                created = await ReconcileSubscriptionAsync(references.SubscriptionReference, exception, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                created = await ReconcileSubscriptionAsync(references.SubscriptionReference, exception, cancellationToken);
            }
            catch (TaskCanceledException exception)
            {
                created = await ReconcileSubscriptionAsync(references.SubscriptionReference, exception, cancellationToken);
            }
            catch (SdkException<CreateSubscriptionError> exception)
            {
                if (exception.Error.TryGetErrorListResponse1(out var errorList))
                {
                    throw ProviderException(422, ValidationMessage("Maxio rejected the subscription request.", errorList.Errors), exception);
                }
                else if (exception.Error.TryGetRawError(out var rawError))
                {
                    throw ProviderException(rawError.StatusCode, "Maxio rejected the subscription request.", exception);
                }

                throw ProviderException(422, "Maxio rejected the subscription request.", exception);
            }
            catch (JsonException exception)
            {
                throw new MaxioSubscriptionException(502, "Maxio returned a response that could not be processed.", exception);
            }

            return await SaveMappingAndReturnAsync(references, customerId, created, product, cancellationToken);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(CancellationToken cancellationToken)
    {
        var identity = GetIdentity();
        var applicationUser = await _userManager.FindByNameAsync(identity)
            ?? throw new MaxioSubscriptionException(401, "The authenticated user could not be found.");
        var references = CreateReferences(applicationUser.Id, identity);
        var mapped = await _identityDb.MaxioSubscriptionMappings
            .SingleOrDefaultAsync(item => item.ApplicationUserId == references.ApplicationUserId, cancellationToken);

        if (mapped is not null)
        {
            var subscription = await ReadSubscriptionAsync(mapped.MaxioSubscriptionId, cancellationToken);
            return new[] { MapSubscription(subscription, null) };
        }

        var customerId = await TryReadCustomerAsync(references.CustomerReference, cancellationToken);
        if (customerId is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await _client.Customers.ListCustomerSubscriptions(customerId.Value, ct: cancellationToken);
        }
        catch (SdkException<RawError> exception)
        {
            throw ProviderException(exception.Error.StatusCode, "Maxio subscriptions could not be loaded.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MaxioSubscriptionException(503, "Maxio is temporarily unavailable.", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new MaxioSubscriptionException(503, "Maxio did not respond in time.", exception);
        }
        catch (JsonException exception)
        {
            throw new MaxioSubscriptionException(502, "Maxio returned a response that could not be processed.", exception);
        }

        return responses
            .Where(response => response.Subscription is not null)
            .Select(response => MapSubscription(response.Subscription!, response.Subscription!.Product))
            .ToArray();
    }

    private async Task<IReadOnlyList<Product>> LoadFamilyProductsAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = new List<Product>();

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    familyId.ToString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: ProductPageSize,
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> exception)
            {
                throw ProviderException(exception.Error.StatusCode, "Maxio subscription plans could not be loaded.", exception);
            }
            catch (HttpRequestException exception)
            {
                throw new MaxioSubscriptionException(503, "Maxio is temporarily unavailable.", exception);
            }
            catch (TaskCanceledException exception)
            {
                throw new MaxioSubscriptionException(503, "Maxio did not respond in time.", exception);
            }
            catch (JsonException exception)
            {
                throw new MaxioSubscriptionException(502, "Maxio returned a response that could not be processed.", exception);
            }

            products.AddRange(pageItems.Where(item => item.Product is not null).Select(item => item.Product!));
            if (pageItems.Count < ProductPageSize)
            {
                break;
            }
        }

        return products;
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioSubscriptionException(503, "Maxio product family configuration is missing.");
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> exception)
        {
            throw ProviderException(exception.Error.StatusCode, "Maxio product families could not be loaded.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MaxioSubscriptionException(503, "Maxio is temporarily unavailable.", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new MaxioSubscriptionException(503, "Maxio did not respond in time.", exception);
        }
        catch (JsonException exception)
        {
            throw new MaxioSubscriptionException(502, "Maxio returned a response that could not be processed.", exception);
        }

        var family = families.FirstOrDefault(item => string.Equals(
            item.ProductFamily?.Handle,
            _options.ProductFamilyHandle,
            StringComparison.OrdinalIgnoreCase));

        if (family?.ProductFamily?.Id is not int familyId)
        {
            throw new MaxioSubscriptionException(404, "The configured Maxio product family was not found.");
        }

        return familyId;
    }

    private async Task<int> EnsureCustomerAsync(string customerReference, string identity, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(customerReference, cancellationToken);
        if (existing is int customerId)
        {
            return customerId;
        }

        var email = identity.Contains('@', StringComparison.Ordinal) ? identity : $"{customerReference}@eshop.local";
        var request = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = identity,
                LastName = "eShopOnWeb",
                Email = email,
                Reference = customerReference
            }
        };

        try
        {
            using (MaxioWriteOnceHandler.Begin())
            {
                var response = await _client.Customers.CreateCustomer(request, ct: cancellationToken);
                return response.Customer.Id
                    ?? throw new MaxioSubscriptionException(502, "Maxio returned a customer without an ID.");
            }
        }
        catch (MaxioWriteAlreadySentException exception)
        {
            return await ReconcileCustomerAsync(customerReference, exception, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return await ReconcileCustomerAsync(customerReference, exception, cancellationToken);
        }
        catch (TaskCanceledException exception)
        {
            return await ReconcileCustomerAsync(customerReference, exception, cancellationToken);
        }
        catch (SdkException<CreateCustomerError> exception)
        {
            var validationPayload = exception.Error.TryGetCustomerErrorResponse1(out var customerError);
            if (validationPayload)
            {
                var messages = new List<string>();
                if (customerError.Errors?.PerPage is not null)
                {
                    messages.AddRange(customerError.Errors.PerPage);
                }
                if (customerError.Errors?.PricePoint is not null)
                {
                    messages.AddRange(customerError.Errors.PricePoint);
                }
                if (messages.Count > 0)
                {
                    throw ProviderException(422, ValidationMessage("Maxio rejected the customer request.", messages), exception);
                }
            }
            else if (exception.Error.TryGetRawError(out var rawError))
            {
                throw ProviderException(rawError.StatusCode, "Maxio rejected the customer request.", exception);
            }

            var duplicate = await TryReadCustomerAsync(customerReference, cancellationToken);
            if (duplicate is int duplicateId)
            {
                return duplicateId;
            }

            throw ProviderException(422, "Maxio rejected the customer request.", exception);
        }
        catch (JsonException exception)
        {
            throw new MaxioSubscriptionException(502, "Maxio returned a response that could not be processed.", exception);
        }
    }

    private async Task<int?> TryReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: cancellationToken);
            return response.Customer.Id
                ?? throw new MaxioSubscriptionException(502, "Maxio returned a customer without an ID.");
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw ProviderException(exception.Error.StatusCode, "Maxio customer data could not be loaded.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MaxioSubscriptionException(503, "Maxio is temporarily unavailable.", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new MaxioSubscriptionException(503, "Maxio did not respond in time.", exception);
        }
        catch (JsonException exception)
        {
            throw new MaxioSubscriptionException(502, "Maxio returned a response that could not be processed.", exception);
        }
    }

    private async Task<int> ReconcileCustomerAsync(string reference, Exception original, CancellationToken cancellationToken)
    {
        try
        {
            var customerId = await TryReadCustomerAsync(reference, cancellationToken);
            if (customerId is int found)
            {
                return found;
            }
        }
        catch (MaxioSubscriptionException exception)
        {
            throw new MaxioSubscriptionException(exception.StatusCode, exception.Message, original);
        }

        throw new MaxioSubscriptionException(503, "Maxio could not confirm the customer enrollment.", original);
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference: reference, ct: cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> exception)
        {
            if (exception.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (exception.Error.TryGetRawError(out var rawError))
            {
                throw ProviderException(rawError.StatusCode, "Maxio subscription data could not be loaded.", exception);
            }

            throw new MaxioSubscriptionException(502, "Maxio returned an unrecognized subscription error.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MaxioSubscriptionException(503, "Maxio is temporarily unavailable.", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new MaxioSubscriptionException(503, "Maxio did not respond in time.", exception);
        }
        catch (JsonException exception)
        {
            throw new MaxioSubscriptionException(502, "Maxio returned a response that could not be processed.", exception);
        }
    }

    private async Task<Subscription> ReconcileSubscriptionAsync(string reference, Exception original, CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await FindSubscriptionAsync(reference, cancellationToken);
            if (subscription is not null)
            {
                return subscription;
            }
        }
        catch (MaxioSubscriptionException exception)
        {
            throw new MaxioSubscriptionException(exception.StatusCode, exception.Message, original);
        }

        throw new MaxioSubscriptionException(503, "Maxio could not confirm the subscription enrollment.", original);
    }

    private async Task<Subscription> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.ReadSubscription(subscriptionId, include: null, ct: cancellationToken);
            return response.Subscription
                ?? throw new MaxioSubscriptionException(502, "Maxio returned an empty subscription response.");
        }
        catch (SdkException<RawError> exception)
        {
            throw ProviderException(exception.Error.StatusCode, "Maxio subscription data could not be loaded.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MaxioSubscriptionException(503, "Maxio is temporarily unavailable.", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw new MaxioSubscriptionException(503, "Maxio did not respond in time.", exception);
        }
        catch (JsonException exception)
        {
            throw new MaxioSubscriptionException(502, "Maxio returned a response that could not be processed.", exception);
        }
    }

    private async Task<SubscriptionDto> SaveMappingAndReturnAsync(
        References references,
        int customerId,
        Subscription subscription,
        Product product,
        CancellationToken cancellationToken)
    {
        if (subscription.Id is not int subscriptionId)
        {
            throw new MaxioSubscriptionException(502, "Maxio returned a subscription without an ID.");
        }

        var now = DateTimeOffset.UtcNow;
        _identityDb.MaxioSubscriptionMappings.Add(new MaxioSubscriptionMapping
        {
            ApplicationUserId = references.ApplicationUserId,
            UserReference = references.UserReference,
            CustomerReference = references.CustomerReference,
            MaxioCustomerId = customerId,
            SubscriptionReference = references.SubscriptionReference,
            MaxioSubscriptionId = subscriptionId,
            ProductHandle = product.Handle!,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            var concurrent = await _identityDb.MaxioSubscriptionMappings
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.ApplicationUserId == references.ApplicationUserId, cancellationToken);
            if (concurrent is not null && string.Equals(concurrent.ProductHandle, product.Handle, StringComparison.OrdinalIgnoreCase))
            {
                return MapSubscription(subscription, product);
            }

            throw new MaxioSubscriptionException(409, "The account already has a subscription.", exception);
        }

        return MapSubscription(subscription, product);
    }

    private static SubscriptionDto MapSubscription(Subscription subscription, Product? fallbackProduct)
    {
        var product = subscription.Product ?? fallbackProduct;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            PlanHandle = product?.Handle,
            PlanName = product?.Name,
            State = subscription.State?.Value,
            PriceInCents = subscription.ProductPriceInCents,
            BillingAmountInCents = subscription.CurrentBillingAmountInCents,
            Currency = subscription.Currency,
            NextBillingDate = subscription.NextAssessmentAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
    }

    private string GetIdentity()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var identity = principal?.FindFirst(ClaimTypes.Name)?.Value ?? principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new MaxioSubscriptionException(401, "An authenticated user is required.");
        }

        return identity.Trim();
    }

    private static References CreateReferences(string applicationUserId, string identity)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToUpperInvariant()));
        var suffix = Convert.ToHexString(bytes).ToLowerInvariant();
        return new References(applicationUserId, $"eshop-user-{suffix}", $"eshop-sub-{suffix}");
    }

    private static MaxioSubscriptionException ProviderException(HttpStatusCode statusCode, string message, Exception inner)
        => ProviderException((int)statusCode, message, inner);

    private static MaxioSubscriptionException ProviderException(int statusCode, string message, Exception inner)
        => new(statusCode is >= 400 and <= 499 ? statusCode : 502, message, inner);

    private static string ValidationMessage(string prefix, IReadOnlyList<string> messages)
    {
        var safeMessages = messages
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Take(3)
            .ToArray();
        return safeMessages.Length == 0 ? prefix : $"{prefix} {string.Join(" ", safeMessages)}";
    }

    private readonly record struct References(string ApplicationUserId, string CustomerReference, string SubscriptionReference)
    {
        public string UserReference => CustomerReference;
    }
}
