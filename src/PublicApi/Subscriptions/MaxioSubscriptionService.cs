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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private static readonly TimeSpan ProviderCallBudget = TimeSpan.FromSeconds(30);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly AppIdentityDbContext _identityContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;
    private readonly Microsoft.Extensions.Logging.ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        AppIdentityDbContext identityContext,
        UserManager<ApplicationUser> userManager,
        Microsoft.Extensions.Options.IOptions<MaxioOptions> options,
        Microsoft.Extensions.Logging.ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _identityContext = identityContext;
        _userManager = userManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = await GetProductFamilyAsync(cancellationToken);
        var plans = new List<SubscriptionPlanDto>();
        const int perPage = 100;

        for (var page = 1; ; page++)
        {
            var products = await Bounded(ct => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: family.Id!.Value.ToString(),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: page,
                perPage: perPage,
                ct: ct), cancellationToken);

            plans.AddRange(products.Select(x => x.Product).Where(x => x is not null).Select(x => new SubscriptionPlanDto
            {
                Handle = x!.Handle ?? string.Empty,
                Name = x.Name ?? string.Empty,
                Description = x.Description,
                PriceInCents = ToNullableInt32(x.PriceInCents),
                Interval = ToNullableInt32(x.Interval),
                IntervalUnit = x.IntervalUnit?.Value
            }));

            if (products.Count < perPage)
                break;
        }

        return plans;
    }

    public async Task<SubscriptionDto> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
            throw new SubscriptionApiException((int)HttpStatusCode.BadRequest, "A productHandle is required.");

        var normalizedHandle = productHandle.Trim();
        var plan = (await GetPlansAsync(cancellationToken)).SingleOrDefault(x =>
            string.Equals(x.Handle, normalizedHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
            throw new SubscriptionApiException((int)HttpStatusCode.NotFound, "The requested subscription plan was not found.");

        var user = await GetUserAsync(username);
        await EnsureCustomerAsync(user, cancellationToken);

        var enrollment = await _identityContext.MaxioSubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == user.Id && x.ProductHandle == normalizedHandle, cancellationToken);
        if (enrollment is not null)
            return await ReconcileExistingEnrollmentAsync(enrollment, cancellationToken);

        enrollment = new MaxioSubscriptionEnrollment
        {
            UserId = user.Id,
            ProductHandle = normalizedHandle,
            SubscriptionReference = SubscriptionReferenceFor(user.Id, normalizedHandle),
            Status = EnrollmentStates.Provisioning,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _identityContext.MaxioSubscriptionEnrollments.Add(enrollment);
        try
        {
            await _identityContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _identityContext.Entry(enrollment).State = EntityState.Detached;
            var admitted = await _identityContext.MaxioSubscriptionEnrollments.SingleAsync(
                x => x.UserId == user.Id && x.ProductHandle == normalizedHandle, cancellationToken);
            return await ReconcileExistingEnrollmentAsync(admitted, cancellationToken);
        }

        var existingSubscription = await FindSubscriptionOrNullAsync(enrollment.SubscriptionReference, cancellationToken);
        if (existingSubscription is not null)
            return await CompleteEnrollmentAsync(enrollment, existingSubscription, cancellationToken);

        try
        {
            SubscriptionResponse created;
            using (MaxioWriteOnceHandler.BeginWrite())
            {
                created = await Bounded(ct => _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = normalizedHandle,
                        CustomerReference = user.MaxioCustomerReference,
                        Reference = enrollment.SubscriptionReference,
                        // The demo plans are deliberately provisioned without card capture.
                        PaymentCollectionMethod = CollectionMethod.Invoice
                    }
                }, ct), cancellationToken);
            }

            return await CompleteEnrollmentAsync(enrollment, created.Subscription, cancellationToken);
        }
        catch (SdkException<CreateSubscriptionError> ex) when (ex.Error.TryGetErrorListResponse1(out var validationError))
        {
            _logger.LogWarning("Maxio rejected subscription for product handle {ProductHandle}: {ValidationErrors}",
                normalizedHandle, string.Join(" | ", validationError.Errors));
            await MarkUnknownAsync(enrollment, cancellationToken);
            throw new SubscriptionApiException((int)HttpStatusCode.UnprocessableEntity, "Maxio rejected this subscription request.", ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            await MarkUnknownAsync(enrollment, cancellationToken);
            throw new SubscriptionApiException((int)HttpStatusCode.Conflict,
                "The subscription outcome is being reconciled. Retry this request shortly.", ex);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            await MarkUnknownAsync(enrollment, cancellationToken);
            throw new SubscriptionApiException((int)HttpStatusCode.BadGateway,
                "Maxio could not process the subscription request.", ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string username, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(username);
        if (user.MaxioCustomerId is null)
            return Array.Empty<SubscriptionDto>();

        try
        {
            var subscriptions = await Bounded(ct => _client.Customers.ListCustomerSubscriptions(user.MaxioCustomerId.Value, ct), cancellationToken);
            return subscriptions.Where(x => x.Subscription is not null).Select(x => ToDto(x.Subscription!)).ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error, "Maxio could not load subscriptions.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new SubscriptionApiException((int)HttpStatusCode.ServiceUnavailable, "Maxio is temporarily unavailable.", ex);
        }
    }

    private async Task<ApplicationUser> GetUserAsync(string username)
        => await _userManager.FindByNameAsync(username)
            ?? throw new SubscriptionApiException((int)HttpStatusCode.Unauthorized, "The authenticated user could not be found.");

    private async Task<ProductFamily> GetProductFamilyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var families = await Bounded(ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct), cancellationToken);
            var family = families.Select(x => x.ProductFamily).SingleOrDefault(x =>
                string.Equals(x?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
            if (family?.Id is null)
                throw new SubscriptionApiException((int)HttpStatusCode.BadGateway, "The configured Maxio product family is unavailable.");
            return family;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error, "Maxio could not load subscription plans.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new SubscriptionApiException((int)HttpStatusCode.ServiceUnavailable, "Maxio is temporarily unavailable.", ex);
        }
    }

    private async Task EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = user.MaxioCustomerReference ?? CustomerReferenceFor(user.Id);
        var customer = await ReadCustomerOrNullAsync(reference, cancellationToken);
        if (customer is null)
        {
            var email = user.Email ?? user.UserName;
            if (string.IsNullOrWhiteSpace(email))
                throw new SubscriptionApiException((int)HttpStatusCode.BadRequest, "A customer email is required to subscribe.");

            try
            {
                CustomerResponse created;
                using (MaxioWriteOnceHandler.BeginWrite())
                {
                    created = await Bounded(ct => _client.Customers.CreateCustomer(new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = "eShop",
                            LastName = "Shopper",
                            Email = email,
                            Reference = reference
                        }
                    }, ct), cancellationToken);
                }
                customer = created.Customer;
            }
            catch (SdkException<CreateCustomerError> ex) when (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                customer = await ReadCustomerOrNullAsync(reference, cancellationToken);
                if (customer is null)
                    throw new SubscriptionApiException((int)HttpStatusCode.UnprocessableEntity, "Maxio rejected the customer request.", ex);
            }
            catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
            {
                customer = await ReadCustomerOrNullAsync(reference, cancellationToken);
                if (customer is null)
                    throw new SubscriptionApiException((int)HttpStatusCode.Conflict,
                        "The customer outcome is being reconciled. Retry this request shortly.", ex);
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                throw new SubscriptionApiException((int)HttpStatusCode.BadGateway, "Maxio could not create the customer.", ex);
            }
        }

        if (customer?.Id is null)
            throw new SubscriptionApiException((int)HttpStatusCode.BadGateway, "Maxio returned an incomplete customer response.");

        user.MaxioCustomerId = customer.Id;
        user.MaxioCustomerReference = reference;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Customer?> ReadCustomerOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Customers.ReadCustomerByReference(reference, ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error, "Maxio could not look up the customer.", ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Subscriptions.FindSubscription(reference, ct), cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            throw new SubscriptionApiException((int)HttpStatusCode.BadGateway, "Maxio could not look up the subscription.", ex);
        }
    }

    private async Task<SubscriptionDto> ReconcileExistingEnrollmentAsync(MaxioSubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        var subscription = await FindSubscriptionOrNullAsync(enrollment.SubscriptionReference, cancellationToken);
        if (subscription is not null)
            return await CompleteEnrollmentAsync(enrollment, subscription, cancellationToken);

        throw new SubscriptionApiException((int)HttpStatusCode.Conflict,
            "A subscription request is already in progress and its outcome is being reconciled.");
    }

    private async Task<SubscriptionDto> CompleteEnrollmentAsync(MaxioSubscriptionEnrollment enrollment, Subscription? subscription, CancellationToken cancellationToken)
    {
        if (subscription?.Id is null)
            throw new SubscriptionApiException((int)HttpStatusCode.BadGateway, "Maxio returned an incomplete subscription response.");

        enrollment.MaxioSubscriptionId = subscription.Id;
        enrollment.Status = EnrollmentStates.Completed;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);
        return ToDto(subscription);
    }

    private async Task MarkUnknownAsync(MaxioSubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        enrollment.Status = EnrollmentStates.Unknown;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionDto ToDto(Subscription subscription)
        => new()
        {
            Id = subscription.Id ?? 0,
            Reference = subscription.Reference ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = ToNullableInt32(subscription.ProductPriceInCents),
            Currency = subscription.Currency,
            State = subscription.State?.Value ?? string.Empty,
            NextBillingDate = subscription.NextAssessmentAt
        };

    private static SubscriptionApiException ProviderFailure(RawError error, string message, Exception inner)
    {
        var status = (int)error.StatusCode;
        return new SubscriptionApiException(status is >= 400 and < 500 ? status : (int)HttpStatusCode.BadGateway, message, inner);
    }

    private static bool IsAmbiguousWriteFailure(Exception ex)
        => ex is HttpRequestException or TaskCanceledException or JsonException or MaxioWriteRetryBlockedException;

    private static int? ToNullableInt32(long? value)
        => value is null ? null : checked((int)value.Value);

    private static string CustomerReferenceFor(string userId) => $"eshop-customer-{Hash(userId)}";
    private static string SubscriptionReferenceFor(string userId, string productHandle) => $"eshop-subscription-{Hash(userId + ":" + productHandle)}";
    private static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ProviderCallBudget);
        return await operation(budget.Token);
    }

    private static class EnrollmentStates
    {
        public const string Provisioning = "provisioning";
        public const string Completed = "completed";
        public const string Unknown = "unknown";
    }
}
