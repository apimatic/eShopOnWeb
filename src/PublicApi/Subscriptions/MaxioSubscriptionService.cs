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

public sealed class MaxioSubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnrollmentLocks = new();
    private readonly MaxioAdvancedBillingClient _client;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> users,
        MaxioOptions options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _identityDb = identityDb;
        _users = users;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        try
        {
            var families = await BoundedAsync(ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct), cancellationToken);
            var family = families.Select(x => x.ProductFamily).FirstOrDefault(x =>
                x is not null && x.ArchivedAt is null && string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));

            if (family?.Id is null)
            {
                throw new MaxioProviderException(HttpStatusCode.NotFound, "The configured Maxio product family was not found.");
            }

            var products = new List<ProductResponse>();
            const int pageSize = 100;
            for (var page = 1; ; page++)
            {
                var response = await BoundedAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.Id.Value.ToString(), dateField: null, filter: null, startDate: null, endDate: null,
                    startDatetime: null, endDatetime: null, includeArchived: false, include: null, page: page, perPage: pageSize, ct: ct), cancellationToken);
                products.AddRange(response);
                if (response.Count < pageSize)
                {
                    break;
                }
            }

            return products
                .Select(x => x.Product)
                .Where(x => x is not null && x.ArchivedAt is null && !string.IsNullOrWhiteSpace(x.Handle) && x.PriceInCents is not null)
                .Select(x => new SubscriptionPlanDto(x.Handle!, x.Name ?? x.Handle!, x.Description, x.PriceInCents!.Value,
                    x.PriceInCents.Value / 100m, x.Interval, x.IntervalUnit?.Value))
                .OrderBy(x => x.PriceInCents)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw new MaxioProviderException(HttpStatusCode.BadGateway, "Maxio rejected the catalog request.", ex);
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioProviderException(HttpStatusCode.BadRequest, "A plan handle is required.");
        }

        var plans = await GetPlansAsync(cancellationToken);
        var plan = plans.SingleOrDefault(x => string.Equals(x.Handle, planHandle, StringComparison.Ordinal));
        if (plan is null)
        {
            throw new MaxioProviderException(HttpStatusCode.BadRequest, "The requested subscription plan is unavailable.");
        }

        var user = await _users.FindByNameAsync(userName) ?? throw new MaxioProviderException(HttpStatusCode.Unauthorized, "The authenticated shopper no longer exists.");
        var key = $"{user.Id}:{plan.Handle}";
        var gate = EnrollmentLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var enrollment = await _identityDb.SubscriptionEnrollments.SingleOrDefaultAsync(
                x => x.UserId == user.Id && x.ProductHandle == plan.Handle, cancellationToken);

            if (enrollment is not null)
            {
                if (enrollment.Status == EnrollmentStatus.Complete)
                {
                    return FromEnrollment(enrollment);
                }

                if (enrollment.Status == EnrollmentStatus.Sending)
                {
                    var recovered = await FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
                    if (recovered is not null)
                    {
                        await CompleteEnrollmentAsync(enrollment, recovered, plan, cancellationToken);
                        return ToDto(recovered, plan);
                    }

                    throw MaxioProviderException.UnknownOutcome();
                }

                if (enrollment.Status == EnrollmentStatus.Failed)
                {
                    throw new MaxioProviderException(HttpStatusCode.UnprocessableEntity, "This enrollment was rejected by Maxio and cannot be retried with the same plan.");
                }
            }
            else
            {
                enrollment = new SubscriptionEnrollment
                {
                    UserId = user.Id,
                    ProductHandle = plan.Handle,
                    CustomerReference = CustomerReference(user.Id),
                    SubscriptionReference = SubscriptionReference(user.Id, plan.Handle),
                    Status = EnrollmentStatus.Prepared,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _identityDb.SubscriptionEnrollments.Add(enrollment);
                try
                {
                    await _identityDb.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    enrollment = await _identityDb.SubscriptionEnrollments.SingleAsync(
                        x => x.UserId == user.Id && x.ProductHandle == plan.Handle, cancellationToken);
                    if (enrollment.Status == EnrollmentStatus.Complete)
                    {
                        return FromEnrollment(enrollment);
                    }

                    throw MaxioProviderException.UnknownOutcome();
                }
            }

            var customer = await EnsureCustomerAsync(user, enrollment, cancellationToken);
            if (customer.Id is null)
            {
                throw new MaxioProviderException(HttpStatusCode.BadGateway, "Maxio returned a customer without an identifier.");
            }

            enrollment.MaxioCustomerId = customer.Id;

            // This also reconciles a previous run when the development in-memory store was reset.
            var existingSubscription = await FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
            if (existingSubscription is not null)
            {
                await CompleteEnrollmentAsync(enrollment, existingSubscription, plan, cancellationToken);
                return ToDto(existingSubscription, plan);
            }

            enrollment.Status = EnrollmentStatus.Sending;
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            await _identityDb.SaveChangesAsync(cancellationToken);

            Subscription? subscription;
            try
            {
                using (SubscriptionWriteOnceHandler.Begin(enrollment.SubscriptionReference))
                {
                    subscription = await CreateSubscriptionAsync(customer.Id, enrollment.SubscriptionReference, plan.Handle, FirstBillingAt(plan), cancellationToken);
                }
            }
            catch (DuplicateSubscriptionWriteAttemptException)
            {
                subscription = await FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
                if (subscription is null)
                {
                    throw MaxioProviderException.UnknownOutcome();
                }
            }
            catch (MaxioProviderException ex) when (!ex.IsClientFailure)
            {
                subscription = await FindSubscriptionAsync(enrollment.SubscriptionReference, cancellationToken);
                if (subscription is null)
                {
                    throw MaxioProviderException.UnknownOutcome();
                }
            }
            catch (MaxioProviderException)
            {
                enrollment.Status = EnrollmentStatus.Failed;
                enrollment.UpdatedAt = DateTimeOffset.UtcNow;
                await _identityDb.SaveChangesAsync(cancellationToken);
                throw;
            }

            if (subscription is null)
            {
                throw MaxioProviderException.UnknownOutcome();
            }

            await CompleteEnrollmentAsync(enrollment, subscription, plan, cancellationToken);
            return ToDto(subscription, plan);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await _users.FindByNameAsync(userName) ?? throw new MaxioProviderException(HttpStatusCode.Unauthorized, "The authenticated shopper no longer exists.");
        var customer = await ReadCustomerAsync(CustomerReference(user.Id), cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        try
        {
            var subscriptions = await BoundedAsync(ct => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct), cancellationToken);
            return subscriptions
                .Select(x => x.Subscription)
                .Where(x => x is not null)
                .Select(x => ToDto(x!, null))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(ApplicationUser user, SubscriptionEnrollment enrollment, CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerAsync(enrollment.CustomerReference, cancellationToken);
        if (existing?.Id is not null)
        {
            return existing;
        }

        var (firstName, lastName, email) = CustomerProfile(user);
        try
        {
            var response = await BoundedAsync(ct => _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = enrollment.CustomerReference
                }
            }, ct), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError>)
        {
            // A reference race is resolved by the provider's unique customer reference.
            existing = await ReadCustomerAsync(enrollment.CustomerReference, cancellationToken);
            if (existing?.Id is not null)
            {
                return existing;
            }

            throw new MaxioProviderException(HttpStatusCode.UnprocessableEntity, "Maxio rejected the customer profile.");
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return (await BoundedAsync(ct => _client.Customers.ReadCustomerByReference(reference, ct), cancellationToken)).Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(ex.Error);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return (await BoundedAsync(ct => _client.Subscriptions.FindSubscription(reference: reference, ct: ct), cancellationToken)).Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            throw new MaxioProviderException(HttpStatusCode.BadGateway, "Maxio could not look up the enrollment.", ex);
        }
    }

    private async Task<Subscription?> CreateSubscriptionAsync(double? customerId, string reference, string planHandle, DateTimeOffset nextBillingAt, CancellationToken cancellationToken)
    {
        try
        {
            return (await BoundedAsync(ct => _client.Subscriptions.CreateSubscription(new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    Reference = reference,
                    NextBillingAt = nextBillingAt
                }
            }, ct), cancellationToken)).Subscription;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                var messages = string.Join(" | ", validation.Errors.Take(10).Select(message =>
                    message.Length <= 512 ? message : message[..512]));
                _logger.LogWarning("Maxio rejected subscription creation for plan {PlanHandle}: {ValidationMessages}", planHandle, messages);
                throw new MaxioProviderException(HttpStatusCode.UnprocessableEntity, "Maxio rejected the subscription request.", ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning("Maxio rejected subscription creation with HTTP status {StatusCode}", (int)raw.StatusCode);
            }

            throw new MaxioProviderException(HttpStatusCode.BadGateway, "Maxio could not create the subscription.", ex);
        }
    }

    private async Task CompleteEnrollmentAsync(SubscriptionEnrollment enrollment, Subscription subscription, SubscriptionPlanDto plan, CancellationToken cancellationToken)
    {
        enrollment.MaxioSubscriptionId = subscription.Id;
        enrollment.PlanName = subscription.Product?.Name ?? plan.Name;
        enrollment.PriceInCents = PriceInCents(subscription) ?? plan.PriceInCents;
        enrollment.Currency = subscription.Currency;
        enrollment.SubscriptionState = subscription.State?.Value;
        enrollment.NextBillingAt = subscription.NextAssessmentAt;
        enrollment.Status = EnrollmentStatus.Complete;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionDto ToDto(Subscription subscription, SubscriptionPlanDto? fallback)
    {
        var cents = PriceInCents(subscription) ?? fallback?.PriceInCents;
        return new SubscriptionDto(subscription.Id, subscription.Reference, subscription.Product?.Handle ?? fallback?.Handle,
            subscription.Product?.Name ?? fallback?.Name, cents, cents is null ? null : cents.Value / 100m,
            subscription.Currency, subscription.State?.Value, subscription.NextAssessmentAt);
    }

    private static SubscriptionDto FromEnrollment(SubscriptionEnrollment enrollment) => new(
        enrollment.MaxioSubscriptionId, enrollment.SubscriptionReference, enrollment.ProductHandle, enrollment.PlanName,
        enrollment.PriceInCents, enrollment.PriceInCents is null ? null : enrollment.PriceInCents.Value / 100m,
        enrollment.Currency, enrollment.SubscriptionState, enrollment.NextBillingAt);

    private static long? PriceInCents(Subscription subscription) => subscription.ProductPriceInCents ?? subscription.CurrentBillingAmountInCents ?? subscription.Product?.PriceInCents;

    private static DateTimeOffset FirstBillingAt(SubscriptionPlanDto plan)
    {
        if (plan.Interval is null || plan.Interval <= 0)
        {
            throw new MaxioProviderException(HttpStatusCode.BadGateway, "Maxio returned a plan without a valid billing interval.");
        }

        return plan.IntervalUnit switch
        {
            "day" => DateTimeOffset.UtcNow.AddDays(plan.Interval.Value),
            "month" when plan.Interval.Value == Math.Truncate(plan.Interval.Value) && plan.Interval.Value <= int.MaxValue =>
                DateTimeOffset.UtcNow.AddMonths((int)plan.Interval.Value),
            _ => throw new MaxioProviderException(HttpStatusCode.BadGateway, "Maxio returned a plan with an unsupported billing interval.")
        };
    }

    private static MaxioProviderException ProviderFailure(RawError error) => new(error.StatusCode, "Maxio could not process the request.");

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            return await call(timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Maxio request did not produce a usable response.");
            throw new MaxioProviderException(HttpStatusCode.BadGateway, "Maxio is temporarily unavailable.", ex);
        }
    }

    private static (string FirstName, string LastName, string Email) CustomerProfile(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new MaxioProviderException(HttpStatusCode.UnprocessableEntity, "The shopper has no email address for subscription billing.");
        }

        var parts = email.Split('@')[0].Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.FirstOrDefault() ?? "Shopper";
        var last = parts.Skip(1).FirstOrDefault() ?? "Customer";
        return (first, last, email);
    }

    private static string CustomerReference(string userId) => $"eshop-user-{Hash(userId)}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-subscription-{Hash($"{userId}:{planHandle}")}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..40];

    private static class EnrollmentStatus
    {
        public const string Prepared = "prepared";
        public const string Sending = "sending";
        public const string Complete = "complete";
        public const string Failed = "failed";
    }
}

public sealed class MaxioProviderException : Exception
{
    public MaxioProviderException(HttpStatusCode statusCode, string message, Exception? innerException = null) : base(message, innerException) => StatusCode = statusCode;
    public HttpStatusCode StatusCode { get; }
    public bool IsClientFailure => (int)StatusCode is >= 400 and < 500;
    public static MaxioProviderException UnknownOutcome() => new(HttpStatusCode.ServiceUnavailable, "The subscription outcome is being reconciled. Retry later; no second enrollment was sent.");
}
