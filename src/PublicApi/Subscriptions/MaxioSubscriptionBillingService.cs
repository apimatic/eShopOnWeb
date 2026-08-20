using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int PageSize = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan OwnershipLease = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private static readonly Regex NamePartPattern = new("[\\p{L}\\p{Nd}]+", RegexOptions.Compiled);

    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly AppIdentityDbContext _identityDb;
    private readonly MaxioRequestContext _requestContext;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        AppIdentityDbContext identityDb,
        MaxioRequestContext requestContext,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _identityDb = identityDb;
        _requestContext = requestContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await GetConfiguredFamilyProductsAsync(cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    private async Task<IReadOnlyList<MaxioAdvancedBilling.Models.Product>> GetConfiguredFamilyProductsAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> familyResponses;
        try
        {
            familyResponses = await ExecuteReadAsync(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio could not return product families.", ex);
        }

        var matchingFamilies = familyResponses
            .Select(x => x.ProductFamily)
            .Where(x => x != null && x.ArchivedAt == null &&
                        string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            .ToList();

        if (matchingFamilies.Count != 1 || matchingFamilies[0]!.Id == null)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.ProviderContract,
                "The configured subscription catalog is unavailable.");
        }

        var familyId = matchingFamilies[0]!.Id!.Value.ToString(CultureInfo.InvariantCulture);
        var products = new List<MaxioAdvancedBilling.Models.Product>();

        for (var page = 1; page <= 100; page++)
        {
            IReadOnlyList<ProductResponse> productResponses;
            try
            {
                productResponses = await ExecuteReadAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyId,
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
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new SubscriptionBillingException(
                        SubscriptionBillingError.NotFound,
                        "The configured subscription catalog was not found.",
                        ex,
                        (int)HttpStatusCode.NotFound);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRawError(raw, "Maxio could not return subscription plans.", ex);
                }

                throw ProviderContract("Maxio returned an unreadable product-list error.", ex);
            }

            foreach (var response in productResponses)
            {
                if (response.Product.ArchivedAt == null)
                {
                    products.Add(response.Product);
                }
            }

            if (productResponses.Count < PageSize)
            {
                break;
            }

            if (page == 100)
            {
                throw ProviderContract("Maxio product pagination exceeded the safety limit.");
            }
        }

        if (products.GroupBy(x => x.Handle, StringComparer.Ordinal).Any(x => x.Count() > 1))
        {
            throw ProviderContract("Maxio returned duplicate product handles.");
        }

        return products;
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ApplicationUser user,
        string productHandle,
        CancellationToken cancellationToken)
    {
        productHandle = productHandle?.Trim() ?? string.Empty;
        if (productHandle.Length == 0)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.InvalidRequest,
                "A productHandle is required.");
        }

        var product = await ReadProductAsync(productHandle, cancellationToken);
        var identity = BuildCustomerIdentity(user);
        var customerReference = BuildReference("eshop-user-v1", user.Id);
        var subscriptionReference = BuildReference("eshop-sub-v1", $"{user.Id}\n{productHandle}");
        var lockKey = $"{user.Id}\n{productHandle}";
        var enrollmentLock = EnrollmentLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await enrollmentLock.WaitAsync(cancellationToken);
        try
        {
            var enrollment = await _identityDb.SubscriptionEnrollments
                .SingleOrDefaultAsync(
                    x => x.UserId == user.Id && x.ProductHandle == productHandle,
                    cancellationToken);

            var ownerToken = Guid.NewGuid().ToString("N");
            var ownsIntent = false;
            if (enrollment == null)
            {
                enrollment = new SubscriptionEnrollment(
                    user.Id,
                    productHandle,
                    customerReference,
                    subscriptionReference,
                    ownerToken,
                    DateTimeOffset.UtcNow.Add(OwnershipLease));
                _identityDb.SubscriptionEnrollments.Add(enrollment);
                try
                {
                    await _identityDb.SaveChangesAsync(cancellationToken);
                    ownsIntent = true;
                }
                catch (DbUpdateException)
                {
                    _identityDb.Entry(enrollment).State = EntityState.Detached;
                    enrollment = await _identityDb.SubscriptionEnrollments
                        .SingleOrDefaultAsync(
                            x => x.UserId == user.Id && x.ProductHandle == productHandle,
                            cancellationToken);
                    if (enrollment == null)
                    {
                        throw;
                    }
                }
            }

            var recovered = await FindSubscriptionAsync(
                subscriptionReference,
                customerReference,
                productHandle,
                cancellationToken);
            if (recovered != null)
            {
                var customerId = RequireCustomerId(recovered.Customer, customerReference);
                var dto = MapSubscription(recovered);
                enrollment.MarkActive(
                    customerId,
                    dto.Id,
                    dto.PlanName,
                    dto.PriceInCents,
                    $"{dto.BillingInterval} {dto.BillingIntervalUnit}",
                    dto.State,
                    dto.NextBillingDate);
                await SaveEnrollmentAsync(cancellationToken);
                return dto;
            }

            if (enrollment.Status == SubscriptionEnrollmentStatus.Active)
            {
                enrollment.MarkFailed();
                await SaveEnrollmentAsync(cancellationToken);
            }

            if (!ownsIntent)
            {
                ownsIntent = enrollment.TryTakeOwnership(
                    ownerToken,
                    DateTimeOffset.UtcNow.Add(OwnershipLease),
                    DateTimeOffset.UtcNow);
                if (ownsIntent)
                {
                    await SaveEnrollmentAsync(cancellationToken);
                }
            }

            if (!ownsIntent || !enrollment.IsOwnedBy(ownerToken))
            {
                throw new SubscriptionBillingException(
                    SubscriptionBillingError.Conflict,
                    "This subscription enrollment is already in progress.");
            }

            var customer = await EnsureCustomerAsync(
                identity,
                customerReference,
                cancellationToken);
            var customerIdValue = RequireCustomerId(customer, customerReference);

            try
            {
                var created = await CreateSubscriptionAsync(
                    productHandle,
                    customerReference,
                    subscriptionReference,
                    cancellationToken);
                ValidateSubscriptionIntent(created, subscriptionReference, customerReference, productHandle);
                var dto = MapSubscription(created);
                enrollment.MarkActive(
                    customerIdValue,
                    dto.Id,
                    dto.PlanName,
                    dto.PriceInCents,
                    $"{dto.BillingInterval} {dto.BillingIntervalUnit}",
                    dto.State,
                    dto.NextBillingDate);
                await SaveEnrollmentAsync(cancellationToken);
                return dto;
            }
            catch (SubscriptionBillingException ex) when (
                ex.Error is SubscriptionBillingError.ProviderRejected or
                    SubscriptionBillingError.Indeterminate)
            {
                var reconciled = await FindSubscriptionAsync(
                    subscriptionReference,
                    customerReference,
                    productHandle,
                    cancellationToken);
                if (reconciled != null)
                {
                    var dto = MapSubscription(reconciled);
                    enrollment.MarkActive(
                        customerIdValue,
                        dto.Id,
                        dto.PlanName,
                        dto.PriceInCents,
                        $"{dto.BillingInterval} {dto.BillingIntervalUnit}",
                        dto.State,
                        dto.NextBillingDate);
                    await SaveEnrollmentAsync(cancellationToken);
                    return dto;
                }

                enrollment.MarkFailed();
                await SaveEnrollmentAsync(cancellationToken);
                throw;
            }
            catch
            {
                enrollment.MarkFailed();
                await SaveEnrollmentAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            enrollmentLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var customerReference = BuildReference("eshop-user-v1", user.Id);
        var customer = await ReadCustomerAsync(customerReference, cancellationToken);
        if (customer == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var customerId = RequireCustomerId(customer, customerReference);
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await ExecuteReadAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio could not return customer subscriptions.", ex);
        }

        var subscriptions = new List<SubscriptionDto>(responses.Count);
        foreach (var response in responses)
        {
            if (response.Subscription == null)
            {
                throw ProviderContract("Maxio returned an empty subscription envelope.");
            }

            subscriptions.Add(MapSubscription(response.Subscription));
        }

        return subscriptions;
    }

    private async Task<MaxioAdvancedBilling.Models.Product> ReadProductAsync(
        string productHandle,
        CancellationToken cancellationToken)
    {
        var products = await GetConfiguredFamilyProductsAsync(cancellationToken);
        var matches = products
            .Where(x => string.Equals(x.Handle, productHandle, StringComparison.Ordinal))
            .ToList();
        if (matches.Count == 0)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.NotFound,
                "The requested subscription plan was not found.",
                null,
                (int)HttpStatusCode.NotFound);
        }

        if (matches.Count != 1)
        {
            throw ProviderContract("Maxio returned duplicate product handles.");
        }

        var product = matches[0];
        if (product.ProductFamily == null ||
            !string.Equals(
                product.ProductFamily.Handle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.NotFound,
                "The requested subscription plan was not found.");
        }

        _ = MapPlan(product);
        return product;
    }

    private async Task<MaxioAdvancedBilling.Models.Customer?> ReadCustomerAsync(
        string customerReference,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteReadAsync(
                ct => _client.Customers.ReadCustomerByReference(customerReference, ct: ct),
                cancellationToken);
            ValidateCustomer(response.Customer, customerReference);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRawError(ex.Error, "Maxio could not read the customer.", ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Customer> EnsureCustomerAsync(
        CustomerIdentity identity,
        string customerReference,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerAsync(customerReference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var request = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new MaxioAdvancedBilling.Models.CreateCustomer
            {
                FirstName = identity.FirstName,
                LastName = identity.LastName,
                Email = identity.Email,
                Reference = customerReference
            }
        };

        try
        {
            var response = await ExecuteWriteAsync(
                ct => _client.Customers.CreateCustomer(body: request, ct: ct),
                cancellationToken);
            ValidateCustomer(response.Customer, customerReference);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var recovered = await ReadCustomerAsync(customerReference, cancellationToken);
                if (recovered != null)
                {
                    return recovered;
                }

                throw new SubscriptionBillingException(
                    SubscriptionBillingError.ProviderRejected,
                    "Maxio rejected the customer record.",
                    ex,
                    (int)HttpStatusCode.UnprocessableEntity);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio could not create the customer.", ex);
            }

            throw ProviderContract("Maxio returned an unreadable customer error.", ex);
        }
        catch (SubscriptionBillingException ex) when (ex.Error == SubscriptionBillingError.Indeterminate)
        {
            var recovered = await ReadCustomerAsync(customerReference, cancellationToken);
            if (recovered == null)
            {
                throw;
            }

            return recovered;
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription?> FindSubscriptionAsync(
        string subscriptionReference,
        string customerReference,
        string productHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ExecuteReadAsync(
                ct => _client.Subscriptions.FindSubscription(subscriptionReference, ct: ct),
                cancellationToken);
            if (response.Subscription == null)
            {
                throw ProviderContract("Maxio returned an empty subscription envelope.");
            }

            ValidateSubscriptionIntent(
                response.Subscription,
                subscriptionReference,
                customerReference,
                productHandle);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw FromRawError(raw, "Maxio could not reconcile the subscription.", ex);
            }

            throw ProviderContract("Maxio returned an unreadable subscription lookup error.", ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Subscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var request = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
            }
        };

        try
        {
            var response = await ExecuteWriteAsync(
                ct => _client.Subscriptions.CreateSubscription(body: request, ct: ct),
                cancellationToken);
            return response.Subscription
                ?? throw ProviderContract("Maxio returned an empty subscription envelope.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out _))
            {
                throw new SubscriptionBillingException(
                    SubscriptionBillingError.ProviderRejected,
                    "Maxio rejected the subscription enrollment.",
                    ex,
                    (int)HttpStatusCode.UnprocessableEntity);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, "Maxio could not create the subscription.", ex);
            }

            throw ProviderContract("Maxio returned an unreadable subscription error.", ex);
        }
    }

    private async Task<T> ExecuteReadAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var scope = _requestContext.Begin(singleWrite: false);
        return await ExecuteBoundedAsync(operation, cancellationToken);
    }

    private async Task<T> ExecuteWriteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var scope = _requestContext.Begin(singleWrite: true);
        try
        {
            return await ExecuteBoundedAsync(operation, cancellationToken);
        }
        catch (MaxioWriteRetryBlockedException ex)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.Indeterminate,
                "The Maxio write outcome is being reconciled.",
                ex);
        }
    }

    private async Task<T> ExecuteBoundedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        try
        {
            return await operation(budget.Token);
        }
        catch (JsonException ex)
        {
            var status = _requestContext.LastStatusCode;
            if (status is >= HttpStatusCode.BadRequest)
            {
                throw new SubscriptionBillingException(
                    SubscriptionBillingError.ProviderRejected,
                    "Maxio rejected the request with an unreadable response.",
                    ex,
                    (int)status.Value);
            }

            throw ProviderContract("Maxio returned a response that could not be processed.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.ProviderUnavailable,
                "Maxio is temporarily unavailable.",
                ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.ProviderUnavailable,
                "The Maxio request timed out.",
                ex);
        }
    }

    private async Task SaveEnrollmentAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "A concurrent subscription enrollment update was detected.");
            throw new SubscriptionBillingException(
                SubscriptionBillingError.Conflict,
                "The subscription enrollment changed concurrently. Retry the request.",
                ex);
        }
    }

    private static SubscriptionPlanDto MapPlan(MaxioAdvancedBilling.Models.Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            product.PriceInCents == null ||
            product.Interval == null ||
            product.IntervalUnit == null)
        {
            throw ProviderContract("Maxio returned an incomplete subscription plan.");
        }

        return new SubscriptionPlanDto(
            product.Handle,
            product.Name,
            product.Description,
            product.PriceInCents.Value,
            product.Interval.Value,
            product.IntervalUnit.Value,
            product.RequestCreditCard == true || product.RequireCreditCard == true);
    }

    private static SubscriptionDto MapSubscription(MaxioAdvancedBilling.Models.Subscription subscription)
    {
        var product = subscription.Product;
        if (subscription.Id == null ||
            product == null ||
            string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            product.Interval == null ||
            product.IntervalUnit == null ||
            subscription.State == null)
        {
            throw ProviderContract("Maxio returned an incomplete subscription.");
        }

        var price = subscription.ProductPriceInCents ?? product.PriceInCents;
        if (price == null)
        {
            throw ProviderContract("Maxio returned a subscription without a price.");
        }

        return new SubscriptionDto(
            subscription.Id.Value,
            product.Handle,
            product.Name,
            price.Value,
            product.Interval.Value,
            product.IntervalUnit.Value,
            subscription.State.Value,
            subscription.NextAssessmentAt,
            subscription.CurrentPeriodEndsAt);
    }

    private static void ValidateCustomer(
        MaxioAdvancedBilling.Models.Customer customer,
        string expectedReference)
    {
        if (customer.Id == null ||
            string.IsNullOrWhiteSpace(customer.Email) ||
            !string.Equals(customer.Reference, expectedReference, StringComparison.Ordinal))
        {
            throw ProviderContract("Maxio returned a mismatched customer.");
        }
    }

    private static int RequireCustomerId(
        MaxioAdvancedBilling.Models.Customer? customer,
        string expectedReference)
    {
        if (customer == null)
        {
            throw ProviderContract("Maxio returned a subscription without a customer.");
        }

        ValidateCustomer(customer, expectedReference);
        return customer.Id!.Value;
    }

    private static void ValidateSubscriptionIntent(
        MaxioAdvancedBilling.Models.Subscription subscription,
        string expectedSubscriptionReference,
        string expectedCustomerReference,
        string expectedProductHandle)
    {
        if (!string.Equals(subscription.Reference, expectedSubscriptionReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.Customer?.Reference, expectedCustomerReference, StringComparison.Ordinal) ||
            !string.Equals(subscription.Product?.Handle, expectedProductHandle, StringComparison.Ordinal))
        {
            throw ProviderContract("Maxio returned a subscription that does not match the enrollment intent.");
        }
    }

    private static SubscriptionPlanDto MapPlanOrThrow(MaxioAdvancedBilling.Models.Product product) => MapPlan(product);

    private static CustomerIdentity BuildCustomerIdentity(ApplicationUser user)
    {
        var email = FirstValidEmail(user.Email, user.UserName);
        if (email == null)
        {
            throw new SubscriptionBillingException(
                SubscriptionBillingError.InvalidRequest,
                "The authenticated account does not have a usable email address.");
        }

        var label = (user.UserName ?? string.Empty).Trim();
        var at = label.IndexOf('@');
        if (at > 0)
        {
            label = label[..at];
        }

        if (label.Length == 0)
        {
            label = email[..email.IndexOf('@')];
        }

        var parts = NamePartPattern.Matches(label).Select(x => x.Value).ToList();
        if (parts.Count > 0)
        {
            return new CustomerIdentity(email, parts[0], parts[^1]);
        }

        var pseudonym = $"user-{Hash(user.Id)[..12]}";
        return new CustomerIdentity(email, pseudonym, pseudonym);
    }

    private static string? FirstValidEmail(params string?[] values)
    {
        foreach (var value in values)
        {
            var candidate = value?.Trim();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                if (string.Equals(new MailAddress(candidate).Address, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            catch (FormatException)
            {
                // Try the next server-side identity value.
            }
        }

        return null;
    }

    private static string BuildReference(string prefix, string value) => $"{prefix}-{Hash(value)[..32]}";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static SubscriptionBillingException FromRawError(
        RawError raw,
        string safeMessage,
        Exception innerException)
    {
        var status = (int)raw.StatusCode;
        var error = status switch
        {
            404 => SubscriptionBillingError.NotFound,
            409 => SubscriptionBillingError.Conflict,
            >= 400 and < 500 => SubscriptionBillingError.ProviderRejected,
            _ => SubscriptionBillingError.ProviderUnavailable
        };
        return new SubscriptionBillingException(error, safeMessage, innerException, status);
    }

    private static SubscriptionBillingException ProviderContract(string message, Exception? inner = null) =>
        new(SubscriptionBillingError.ProviderContract, message, inner);

    private sealed record CustomerIdentity(string Email, string FirstName, string LastName);
}
