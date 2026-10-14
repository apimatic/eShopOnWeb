using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Customers;
using MaxioAdvancedBilling.Requests.ProductFamilies;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioSubscriptionService
{
    private const int PlansPageSize = 200;
    private const int MaximumPlanPages = 5;
    private readonly MaxioAdvancedBillingClient _client;
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> userManager, IOptions<MaxioOptions> options,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _identityDb = identityDb;
        _userManager = userManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SubscriptionPlansResponse> GetPlansAsync(CancellationToken cancellationToken)
    {
        var productFamilyId = await GetProductFamilyIdAsync(cancellationToken);
        var plans = new List<SubscriptionPlanDto>();
        var isTruncated = false;
        for (var page = 1; page <= MaximumPlanPages; page++)
        {
            IReadOnlyList<ProductResponse> response;
            try
            {
                response = await _client.ProductFamilies.ListProductsForProductFamily(
                    new ListProductsForProductFamilyRequest { ProductFamilyId = productFamilyId.ToString(), Page = page, PerPage = PlansPageSize },
                    cancellationToken: cancellationToken);
            }
            catch (SdkException ex)
            {
                throw ProviderFailure("listing subscription plans", ex);
            }

            plans.AddRange(response.Where(x => x.Product?.Handle is not null).Select(x => new SubscriptionPlanDto(
                x.Product!.Handle!, x.Product.Name ?? x.Product.Handle!, x.Product.PriceInCents ?? 0,
                x.Product.Interval, x.Product.IntervalUnit?.Value)));
            if (response.Count < PlansPageSize)
                break;
            if (page == MaximumPlanPages)
                isTruncated = true;
        }

        return new SubscriptionPlansResponse(plans, isTruncated);
    }

    private async Task<int> GetProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(new ListProductFamiliesRequest(), cancellationToken: cancellationToken);
            var family = families.Select(x => x.ProductFamily).FirstOrDefault(x =>
                x?.Id is not null && string.Equals(x.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));
            return family?.Id ?? throw new ProviderFailureException("Configured Maxio product family was not found.");
        }
        catch (SdkException ex)
        {
            throw ProviderFailure("resolving configured product family", ex);
        }
    }

    public async Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
            throw new SubscriptionRequestException(HttpStatusCode.BadRequest, "planHandle is required.");

        var user = await GetUserAsync(principal);
        var plans = await GetPlansAsync(cancellationToken);
        if (!plans.Plans.Any(x => string.Equals(x.Handle, planHandle, StringComparison.Ordinal)))
            throw new SubscriptionRequestException(HttpStatusCode.BadRequest, "The requested plan is not available.");

        var customerId = await EnsureCustomerAsync(user, cancellationToken);
        var reference = SubscriptionReference(user.Id, planHandle);
        var existing = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
            return existing;
        var claim = new SubscriptionOperationClaim
        {
            Key = $"subscription:{user.Id}:{planHandle}", Reference = reference, CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            _identityDb.SubscriptionOperationClaims.Add(claim);
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (IsClaimConflict(ex))
        {
            _identityDb.Entry(claim).State = EntityState.Detached;
            return await ResolveExistingSubscriptionAsync(reference, cancellationToken);
        }

        try
        {
            var result = await _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionOperationRequest
                {
                    Body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription { CustomerId = customerId, ProductHandle = planHandle, Reference = reference, PaymentCollectionMethod = CollectionMethod.Invoice }
                    }
                }, cancellationToken: cancellationToken);
            var subscription = result.Subscription ?? throw new ProviderFailureException("Maxio created a subscription without a subscription body.");
            claim.Status = "Completed";
            claim.MaxioSubscriptionId = subscription.Id;
            claim.CompletedAt = DateTimeOffset.UtcNow;
            await _identityDb.SaveChangesAsync(cancellationToken);
            return ToDto(subscription);
        }
        catch (SdkConnectionException)
        {
            return await ResolveExistingSubscriptionAsync(reference, cancellationToken);
        }
        catch (ResponseDeserializationException)
        {
            return await ResolveExistingSubscriptionAsync(reference, cancellationToken);
        }
        catch (ApiException<CreateSubscriptionError> ex)
        {
            _identityDb.SubscriptionOperationClaims.Remove(claim);
            await _identityDb.SaveChangesAsync(cancellationToken);
            if (ex.Error.TryGetErrorListResponse1(out var errors))
                throw new SubscriptionRequestException(HttpStatusCode.BadRequest, string.Join(" ", errors.Errors));
            throw ProviderFailure("creating subscription", ex);
        }
        catch (SdkException ex)
        {
            _identityDb.SubscriptionOperationClaims.Remove(claim);
            await _identityDb.SaveChangesAsync(cancellationToken);
            throw ProviderFailure("creating subscription", ex);
        }
    }

    public async Task<MySubscriptionsResponse> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        var customerId = await FindCustomerIdAsync(CustomerReference(user.Id), cancellationToken);
        if (customerId is null)
            return new MySubscriptionsResponse([]);

        try
        {
            var results = await _client.Customers.ListCustomerSubscriptions(
                new ListCustomerSubscriptionsRequest { CustomerId = customerId.Value }, cancellationToken: cancellationToken);
            return new MySubscriptionsResponse(results.Where(x => x.Subscription is not null).Select(x => ToDto(x.Subscription!)).ToList());
        }
        catch (SdkException ex)
        {
            throw ProviderFailure("listing subscriptions", ex);
        }
    }

    private async Task<int> EnsureCustomerAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(user.Id);
        var existingId = await FindCustomerIdAsync(reference, cancellationToken);
        if (existingId is not null)
            return existingId.Value;

        var claim = new SubscriptionOperationClaim { Key = $"customer:{user.Id}", Reference = reference, CreatedAt = DateTimeOffset.UtcNow };
        try
        {
            _identityDb.SubscriptionOperationClaims.Add(claim);
            await _identityDb.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (IsClaimConflict(ex))
        {
            _identityDb.Entry(claim).State = EntityState.Detached;
            var claimedCustomerId = await FindCustomerIdAsync(reference, cancellationToken);
            if (claimedCustomerId is not null)
                return claimedCustomerId.Value;
            throw new SubscriptionRequestException(HttpStatusCode.Conflict, "Customer enrollment is already being processed.");
        }

        try
        {
            var names = (user.UserName ?? "Shopper").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var response = await _client.Customers.CreateCustomer(new CreateCustomerOperationRequest
            {
                Body = new CreateCustomerRequest
                {
                    Customer = new CreateCustomer { FirstName = names.FirstOrDefault() ?? "Shopper", LastName = names.Skip(1).FirstOrDefault() ?? "User", Email = user.Email ?? user.UserName ?? $"{user.Id}@invalid.local", Reference = reference }
                }
            }, cancellationToken: cancellationToken);
            var id = response.Customer.Id ?? throw new ProviderFailureException("Maxio created a customer without an id.");
            claim.Status = "Completed";
            claim.MaxioCustomerId = id;
            claim.CompletedAt = DateTimeOffset.UtcNow;
            await _identityDb.SaveChangesAsync(cancellationToken);
            return id;
        }
        catch (SdkConnectionException)
        {
            var id = await FindCustomerIdAsync(reference, cancellationToken);
            if (id is not null) return id.Value;
            throw new SubscriptionRequestException(HttpStatusCode.ServiceUnavailable, "Customer enrollment outcome is pending; retry this request shortly.");
        }
        catch (ResponseDeserializationException)
        {
            var id = await FindCustomerIdAsync(reference, cancellationToken);
            if (id is not null) return id.Value;
            throw new SubscriptionRequestException(HttpStatusCode.ServiceUnavailable, "Customer enrollment outcome is pending; retry this request shortly.");
        }
        catch (SdkException ex)
        {
            _identityDb.SubscriptionOperationClaims.Remove(claim);
            await _identityDb.SaveChangesAsync(cancellationToken);
            throw ProviderFailure("creating customer", ex);
        }
    }

    private async Task<int?> FindCustomerIdAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(new ReadCustomerByReferenceRequest { Reference = reference }, cancellationToken: cancellationToken);
            return response.Customer.Id;
        }
        catch (SdkException ex) when (IsNotFound(ex)) { return null; }
        catch (SdkException ex) { throw ProviderFailure("reading customer", ex); }
    }

    private async Task<SubscriptionDto> ResolveExistingSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var existing = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
        return existing ?? throw new SubscriptionRequestException(HttpStatusCode.Conflict, "Subscription enrollment is already being processed.");
    }

    private async Task<SubscriptionDto?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(new FindSubscriptionRequest { Reference = reference }, cancellationToken: cancellationToken);
            return ToDto(response.Subscription ?? throw new ProviderFailureException("Maxio returned an empty subscription."));
        }
        catch (SdkException ex) when (IsNotFound(ex))
        {
            return null;
        }
        catch (SdkException ex) { throw ProviderFailure("reading subscription", ex); }
    }

    private async Task<ApplicationUser> GetUserAsync(ClaimsPrincipal principal)
    {
        var username = principal.FindFirstValue(ClaimTypes.Name);
        var user = username is null ? null : await _userManager.FindByNameAsync(username);
        return user ?? throw new SubscriptionRequestException(HttpStatusCode.Unauthorized, "Authenticated user was not found.");
    }

    private static bool IsNotFound(SdkException exception) => exception is ApiException api && api.StatusCode == HttpStatusCode.NotFound;
    private static bool IsClaimConflict(Exception exception) => exception is DbUpdateException || exception is ArgumentException;
    private ProviderFailureException ProviderFailure(string operation, SdkException exception)
    {
        _logger.LogWarning(exception, "Maxio failure while {Operation}; request {Method} {Uri}", operation, exception.Method, exception.RequestUri);
        return new ProviderFailureException($"Maxio failed while {operation}.", exception);
    }
    private static string CustomerReference(string userId) => $"eshop-user-{userId}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-subscription-{userId}-{planHandle}";
    private static SubscriptionDto ToDto(Subscription subscription) => new(subscription.Id, subscription.Product?.Handle, subscription.Product?.Name, subscription.ProductPriceInCents, subscription.State?.Value, subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
}

public sealed class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
    public HttpStatusCode StatusCode { get; }
}
public sealed class ProviderFailureException : Exception
{
    public ProviderFailureException(string message, Exception? innerException = null) : base(message, innerException) { }
}
