using System;
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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private const int MaxioCallBudgetSeconds = 25;
    private readonly MaxioAdvancedBillingClient _client;
    private readonly AppIdentityDbContext _identityContext;
    private readonly SubscriptionEnrollmentLock _enrollmentLock;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        AppIdentityDbContext identityContext,
        SubscriptionEnrollmentLock enrollmentLock,
        ILogger<MaxioSubscriptionBillingService> logger,
        IOptions<MaxioOptions> options)
    {
        _client = client;
        _identityContext = identityContext;
        _enrollmentLock = enrollmentLock;
        _logger = logger;
        _options = options.Value;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken) =>
        RunAsync(GetPlansCoreAsync, cancellationToken);

    public Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken) =>
        RunAsync(ct => GetSubscriptionsCoreAsync(userId, ct), cancellationToken);

    public Task<SubscriptionDto> SubscribeAsync(Shopper shopper, string planHandle, CancellationToken cancellationToken) =>
        RunAsync(ct => SubscribeCoreAsync(shopper, planHandle, ct), cancellationToken);

    private async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansCoreAsync(CancellationToken cancellationToken)
    {
        var family = await GetConfiguredFamilyAsync(cancellationToken);
        var familyId = family.Id?.ToString();
        if (string.IsNullOrWhiteSpace(familyId))
        {
            throw new BillingException(StatusCodes.Status502BadGateway, "Maxio returned a product family without an identifier.");
        }

        var result = new List<SubscriptionPlanDto>();
        const int pageSize = 100;
        for (var page = 1; page <= 100; page++)
        {
            var products = await BoundedAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
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
                perPage: pageSize,
                ct: ct), cancellationToken);

            foreach (var product in products.Select(response => response.Product).Where(product => product is not null))
            {
                if (string.IsNullOrWhiteSpace(product!.Handle) || string.IsNullOrWhiteSpace(product.Name))
                {
                    continue;
                }

                result.Add(new SubscriptionPlanDto(
                    product.Handle,
                    product.Name,
                    (product.PriceInCents ?? 0) / 100m,
                    product.Interval,
                    product.IntervalUnit?.Value,
                    true));
            }

            if (products.Count < pageSize)
            {
                break;
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsCoreAsync(string userId, CancellationToken cancellationToken)
    {
        var customer = await FindCustomerAsync(userId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await BoundedAsync(ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct), cancellationToken);
        return subscriptions
            .Select(response => response.Subscription)
            .Where(subscription => subscription is not null)
            .Select(subscription => ToDto(subscription!))
            .ToArray();
    }

    private async Task<SubscriptionDto> SubscribeCoreAsync(Shopper shopper, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new BillingException(StatusCodes.Status400BadRequest, "A plan handle is required.");
        }

        if (string.IsNullOrWhiteSpace(shopper.Email))
        {
            throw new BillingException(StatusCodes.Status400BadRequest, "The signed-in user must have an email address before subscribing.");
        }

        var plan = (await GetPlansCoreAsync(cancellationToken))
            .SingleOrDefault(candidate => string.Equals(candidate.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new BillingException(StatusCodes.Status400BadRequest, "The requested plan is not available in the configured Maxio product family.");
        }

        var reference = SubscriptionReference(shopper.UserId, plan.Handle);
        using var held = await _enrollmentLock.AcquireAsync($"{shopper.UserId}:{plan.Handle}", cancellationToken);
        var enrollment = await GetOrCreateEnrollmentAsync(shopper.UserId, plan.Handle, reference, cancellationToken);

        if (!string.Equals(enrollment.SubscriptionReference, reference, StringComparison.Ordinal))
        {
            var legacySubscription = await FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
            if (legacySubscription is not null)
            {
                await CompleteEnrollmentAsync(enrollment, legacySubscription, cancellationToken);
                return ToDto(legacySubscription, plan);
            }

            enrollment.SubscriptionReference = reference;
            enrollment.SubscriptionWriteAttemptedAt = null;
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            await _identityContext.SaveChangesAsync(cancellationToken);
        }

        var existing = await FindSubscriptionAsync(reference, cancellationToken);
        if (existing is not null)
        {
            await CompleteEnrollmentAsync(enrollment, existing, cancellationToken);
            return ToDto(existing, plan);
        }

        if (enrollment.MaxioSubscriptionId is not null)
        {
            throw new BillingException(StatusCodes.Status409Conflict, "The subscription is still being reconciled. Retry shortly.");
        }

        if (enrollment.SubscriptionWriteAttemptedAt is not null)
        {
            throw new BillingException(StatusCodes.Status409Conflict, "The prior subscription request is still being reconciled. Retry shortly.");
        }

        var customer = await GetOrCreateCustomerAsync(shopper, cancellationToken);
        var paymentCollectionMethod = await GetNoCardPaymentCollectionMethodAsync(cancellationToken);
        enrollment.MaxioCustomerId = customer.Id;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);

        enrollment.SubscriptionWriteAttemptedAt = DateTimeOffset.UtcNow;
        enrollment.UpdatedAt = enrollment.SubscriptionWriteAttemptedAt.Value;
        await _identityContext.SaveChangesAsync(cancellationToken);

        Subscription? subscription;
        try
        {
            using (MaxioWriteAttemptGuard.BeginScope())
            {
                var created = await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        CustomerId = customer.Id,
                        ProductHandle = plan.Handle,
                        Reference = reference,
                        PaymentCollectionMethod = paymentCollectionMethod
                    }
                }, ct), cancellationToken);
                subscription = created.Subscription;
            }
        }
        catch (SdkException<CreateSubscriptionError> exception)
        {
            throw CreateSubscriptionFailure(exception);
        }
        catch (MaxioWriteRetryBlockedException)
        {
            subscription = await FindSubscriptionAsync(reference, cancellationToken);
            if (subscription is null)
            {
                throw new BillingException(StatusCodes.Status502BadGateway, "Maxio did not confirm the subscription. It will be reconciled before another enrollment is attempted.");
            }
        }

        if (subscription is null)
        {
            throw new BillingException(StatusCodes.Status502BadGateway, "Maxio returned a subscription response without a subscription.");
        }

        await CompleteEnrollmentAsync(enrollment, subscription, cancellationToken);
        return ToDto(subscription, plan);
    }

    private async Task<ProductFamily> GetConfiguredFamilyAsync(CancellationToken cancellationToken)
    {
        var families = await BoundedAsync(ct => _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: ct), cancellationToken);

        var handle = _options.ProductFamilyHandle;
        return families
            .Select(response => response.ProductFamily)
            .FirstOrDefault(family => family is not null && string.Equals(family.Handle, handle, StringComparison.OrdinalIgnoreCase))
            ?? throw new BillingException(StatusCodes.Status500InternalServerError, "The configured Maxio product family could not be found.");
    }

    private async Task<Customer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return (await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(reference, ct), cancellationToken)).Customer;
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Customer> GetOrCreateCustomerAsync(Shopper shopper, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerAsync(shopper.UserId, cancellationToken);
        if (existing?.Id is not null)
        {
            await UpsertCustomerMappingAsync(shopper.UserId, existing, cancellationToken);
            return existing;
        }

        var (firstName, lastName) = CustomerName(shopper);
        CustomerResponse created;
        try
        {
            using (MaxioWriteAttemptGuard.BeginScope())
            {
                created = await BoundedAsync(ct => _client.Customers.CreateCustomer(new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = shopper.Email,
                        Reference = shopper.UserId
                    }
                }, ct), cancellationToken);
            }
        }
        catch (SdkException<CreateCustomerError> exception)
        {
            if (exception.Error.TryGetCustomerErrorResponse1(out _))
            {
                var racedCustomer = await FindCustomerAsync(shopper.UserId, cancellationToken);
                if (racedCustomer?.Id is not null)
                {
                    await UpsertCustomerMappingAsync(shopper.UserId, racedCustomer, cancellationToken);
                    return racedCustomer;
                }
            }

            throw CreateCustomerFailure(exception);
        }
        catch (MaxioWriteRetryBlockedException)
        {
            var reconciled = await FindCustomerAsync(shopper.UserId, cancellationToken);
            if (reconciled?.Id is not null)
            {
                await UpsertCustomerMappingAsync(shopper.UserId, reconciled, cancellationToken);
                return reconciled;
            }

            throw new BillingException(StatusCodes.Status502BadGateway, "Maxio did not confirm the customer. Retry after the customer reference is reconciled.");
        }

        if (created.Customer?.Id is null)
        {
            throw new BillingException(StatusCodes.Status502BadGateway, "Maxio returned a customer response without a customer.");
        }

        await UpsertCustomerMappingAsync(shopper.UserId, created.Customer, cancellationToken);
        return created.Customer;
    }

    private async Task<CollectionMethod> GetNoCardPaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var site = (await BoundedAsync(ct => _client.Sites.ReadSite(ct), cancellationToken)).Site;
        if (site is null)
        {
            throw new BillingException(StatusCodes.Status502BadGateway, "Maxio returned a site response without a site.");
        }

        return site.RelationshipInvoicingEnabled is true
            ? CollectionMethod.Remittance
            : CollectionMethod.Invoice;
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return (await BoundedAsync(ct => _client.Subscriptions.FindSubscription(reference, ct), cancellationToken)).Subscription;
        }
        catch (SdkException<FindSubscriptionError> exception) when (exception.Error.TryGetNoContent(out _))
        {
            return null;
        }
    }

    private async Task<MaxioSubscriptionEnrollment> GetOrCreateEnrollmentAsync(string userId, string planHandle, string reference, CancellationToken cancellationToken)
    {
        var enrollment = await _identityContext.MaxioSubscriptionEnrollments
            .SingleOrDefaultAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        if (enrollment is not null)
        {
            return enrollment;
        }

        enrollment = new MaxioSubscriptionEnrollment
        {
            UserId = userId,
            PlanHandle = planHandle,
            SubscriptionReference = reference,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _identityContext.MaxioSubscriptionEnrollments.Add(enrollment);

        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            _identityContext.ChangeTracker.Clear();
            return await _identityContext.MaxioSubscriptionEnrollments
                .SingleAsync(item => item.UserId == userId && item.PlanHandle == planHandle, cancellationToken);
        }
    }

    private async Task CompleteEnrollmentAsync(MaxioSubscriptionEnrollment enrollment, Subscription subscription, CancellationToken cancellationToken)
    {
        if (subscription.Id is null)
        {
            throw new BillingException(StatusCodes.Status502BadGateway, "Maxio returned a subscription without an identifier.");
        }

        enrollment.MaxioSubscriptionId = subscription.Id;
        enrollment.MaxioCustomerId ??= subscription.Customer?.Id;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertCustomerMappingAsync(string userId, Customer customer, CancellationToken cancellationToken)
    {
        if (customer.Id is null)
        {
            return;
        }

        var mapping = await _identityContext.MaxioBillingCustomers.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (mapping is null)
        {
            mapping = new MaxioBillingCustomer { UserId = userId };
            _identityContext.MaxioBillingCustomers.Add(mapping);
        }

        mapping.MaxioCustomerId = customer.Id.Value;
        mapping.Reference = userId;
        mapping.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionDto ToDto(Subscription subscription, SubscriptionPlanDto? fallbackPlan = null) => new(
        subscription.Id ?? 0,
        subscription.Reference ?? string.Empty,
        subscription.Product?.Handle ?? fallbackPlan?.Handle,
        subscription.Product?.Name ?? fallbackPlan?.Name,
        subscription.ProductPriceInCents is null ? null : subscription.ProductPriceInCents.Value / 100m,
        subscription.Currency,
        subscription.State?.Value,
        subscription.NextAssessmentAt);

    private static (string FirstName, string LastName) CustomerName(Shopper shopper)
    {
        var localPart = shopper.Email.Split('@', 2)[0];
        var parts = localPart.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = parts.FirstOrDefault() ?? shopper.UserName;
        var lastName = parts.Skip(1).FirstOrDefault() ?? "Shopper";
        return (Clip(firstName, 50), Clip(lastName, 50));
    }

    private static string SubscriptionReference(string userId, string planHandle)
    {
        var identity = $"{userId}\n{planHandle}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return $"eshop-{hash[..24]}";
    }

    private static string Clip(string value, int length) => value.Length <= length ? value : value[..length];

    private static BillingException CreateCustomerFailure(SdkException<CreateCustomerError> exception)
    {
        if (exception.Error.TryGetCustomerErrorResponse1(out _))
        {
            return new BillingException(StatusCodes.Status422UnprocessableEntity, "Maxio rejected the customer details.", exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return new BillingException(StatusCodes.Status502BadGateway, "Maxio rejected the customer request.", exception);
    }

    private BillingException CreateSubscriptionFailure(SdkException<CreateSubscriptionError> exception)
    {
        if (exception.Error.TryGetErrorListResponse1(out var details))
        {
            _logger.LogWarning("Maxio rejected CreateSubscription validation: {ValidationErrors}", string.Join("; ", details.Errors));
            return new BillingException(StatusCodes.Status422UnprocessableEntity, "Maxio rejected the subscription request.", exception);
        }

        if (exception.Error.TryGetRawError(out var raw))
        {
            return FromRawError(raw, exception);
        }

        return new BillingException(StatusCodes.Status502BadGateway, "Maxio rejected the subscription request.", exception);
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await operation(cancellationToken);
        }
        catch (SdkException<RawError> exception)
        {
            throw FromRawError(exception.Error, exception);
        }
        catch (SdkException<FindSubscriptionError> exception)
        {
            if (exception.Error.TryGetRawError(out var raw))
            {
                throw FromRawError(raw, exception);
            }

            throw new BillingException(StatusCodes.Status502BadGateway, "Maxio rejected the subscription lookup.", exception);
        }
    }

    private static BillingException FromRawError(RawError error, Exception innerException) => new(
        (int)error.StatusCode is >= 400 and < 500 ? (int)error.StatusCode : StatusCodes.Status502BadGateway,
        (int)error.StatusCode is >= 400 and < 500 ? "Maxio rejected the request." : "Maxio is temporarily unavailable.",
        innerException);

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(MaxioCallBudgetSeconds));
        try
        {
            return await call(budget.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Maxio did not complete a request within the billing service boundary.");
            throw new BillingException(StatusCodes.Status502BadGateway, "Maxio is temporarily unavailable.", exception);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Maxio returned a response that could not be processed.");
            throw new BillingException(StatusCodes.Status502BadGateway, "Maxio returned a response that could not be processed.", exception);
        }
    }
}
