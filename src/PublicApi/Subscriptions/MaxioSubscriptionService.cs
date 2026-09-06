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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Application boundary for all Maxio calls and subscription idempotency.</summary>
public sealed class MaxioSubscriptionService
{
    private const int PageSize = 100;
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly MaxioPostSendGate _postSendGate;
    private readonly AppIdentityDbContext _identityContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioOptions options,
        MaxioPostSendGate postSendGate,
        AppIdentityDbContext identityContext,
        UserManager<ApplicationUser> userManager)
    {
        _client = client;
        _options = options;
        _postSendGate = postSendGate;
        _identityContext = identityContext;
        _userManager = userManager;
    }

    public async Task<SubscriptionPlanResponse> ListPlansAsync(CancellationToken ct)
    {
        var family = await GetProductFamilyAsync(ct);
        var plans = new List<SubscriptionPlanDto>();

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> result;
            try
            {
                result = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.Id!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
                    ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> exception)
            {
                throw ToProviderException(exception);
            }
            catch (Exception exception) when (IsConnectivityFailure(exception))
            {
                throw ProviderUnavailable(exception);
            }
            catch (JsonException exception)
            {
                throw ProviderMalformed(exception);
            }

            plans.AddRange(result
                .Where(response => response.Product is { ArchivedAt: null } product && !string.IsNullOrWhiteSpace(product.Handle))
                .Select(response => ToPlanDto(response.Product!)));

            if (result.Count < PageSize)
            {
                break;
            }
        }

        return new SubscriptionPlanResponse { Plans = plans };
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string planHandle, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new MaxioProviderException("A subscription plan handle is required.", HttpStatusCode.BadRequest);
        }

        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new MaxioProviderException("The authenticated user no longer exists.", HttpStatusCode.Unauthorized);
        if (string.IsNullOrWhiteSpace(user.Email) || string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName))
        {
            throw new MaxioProviderException("Your account needs a first name, last name, and email before it can subscribe.", HttpStatusCode.UnprocessableEntity);
        }

        var plan = await FindPlanAsync(planHandle, ct)
            ?? throw new MaxioProviderException("The requested subscription plan is unavailable.", HttpStatusCode.BadRequest);

        var customerReference = CustomerReference(user.Id);
        await EnsureCustomerAsync(user, customerReference, ct);

        var normalizedPlanHandle = plan.Handle!;
        var subscriptionReference = SubscriptionReference(user.Id, normalizedPlanHandle);
        var ownsClaim = await TryClaimEnrollmentAsync(user.Id, normalizedPlanHandle, subscriptionReference, ct);

        var existing = await TryFindSubscriptionAsync(subscriptionReference, ct);
        if (existing is not null)
        {
            await ConfirmEnrollmentAsync(user.Id, normalizedPlanHandle, existing.Id, ct);
            return ToSubscriptionDto(existing);
        }

        if (!ownsClaim)
        {
            existing = await WaitForSubscriptionAsync(subscriptionReference, ct);
            if (existing is null)
            {
                throw new SubscriptionEnrollmentInProgressException();
            }

            return ToSubscriptionDto(existing);
        }

        try
        {
            SubscriptionResponse created;
            using (_postSendGate.BeginScope())
            {
                created = await _client.Subscriptions.CreateSubscription(new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = normalizedPlanHandle,
                        CustomerReference = customerReference,
                        Reference = subscriptionReference
                    }
                }, ct: ct);
            }

            if (created.Subscription is null)
            {
                throw new MaxioProviderException("The billing provider returned an incomplete subscription response.", indeterminate: true);
            }

            await ConfirmEnrollmentAsync(user.Id, normalizedPlanHandle, created.Subscription.Id, ct);
            return ToSubscriptionDto(created.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> exception)
        {
            throw ToProviderException(exception);
        }
        catch (Exception exception) when (exception is MaxioPostRetryBlockedException || IsConnectivityFailure(exception))
        {
            var reconciled = await TryFindSubscriptionAsync(subscriptionReference, ct);
            if (reconciled is not null)
            {
                await ConfirmEnrollmentAsync(user.Id, normalizedPlanHandle, reconciled.Id, ct);
                return ToSubscriptionDto(reconciled);
            }

            throw ProviderUnavailable(exception);
        }
        catch (JsonException exception)
        {
            throw ProviderMalformed(exception);
        }
    }

    public async Task<MySubscriptionsResponse> ListMySubscriptionsAsync(string userName, CancellationToken ct)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new MaxioProviderException("The authenticated user no longer exists.", HttpStatusCode.Unauthorized);
        var customer = await TryReadCustomerAsync(CustomerReference(user.Id), ct);
        if (customer is null || customer.Id is null)
        {
            return new MySubscriptionsResponse();
        }

        try
        {
            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct);
            return new MySubscriptionsResponse
            {
                Subscriptions = subscriptions
                    .Where(response => response.Subscription is not null)
                    .Select(response => ToSubscriptionDto(response.Subscription!))
                    .ToList()
            };
        }
        catch (SdkException<RawError> exception)
        {
            throw ToProviderException(exception);
        }
        catch (Exception exception) when (IsConnectivityFailure(exception))
        {
            throw ProviderUnavailable(exception);
        }
        catch (JsonException exception)
        {
            throw ProviderMalformed(exception);
        }
    }

    private async Task<ProductFamily> GetProductFamilyAsync(CancellationToken ct)
    {
        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(null, null, null, null, null, ct: ct);
            return families.Select(response => response.ProductFamily)
                .FirstOrDefault(family => family is not null && string.Equals(family.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                ?? throw new MaxioProviderException("The configured subscription product family was not found.", HttpStatusCode.ServiceUnavailable);
        }
        catch (SdkException<RawError> exception)
        {
            throw ToProviderException(exception);
        }
        catch (Exception exception) when (IsConnectivityFailure(exception))
        {
            throw ProviderUnavailable(exception);
        }
        catch (JsonException exception)
        {
            throw ProviderMalformed(exception);
        }
    }

    private async Task<Product?> FindPlanAsync(string planHandle, CancellationToken ct)
    {
        var family = await GetProductFamilyAsync(ct);
        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> result;
            try
            {
                result = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.Id!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null,
                    includeArchived: false, include: null, page: page, perPage: PageSize, ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> exception)
            {
                throw ToProviderException(exception);
            }

            var product = result.Select(item => item.Product).FirstOrDefault(item => item is { ArchivedAt: null } && string.Equals(item.Handle, planHandle, StringComparison.Ordinal));
            if (product is not null)
            {
                return product;
            }

            if (result.Count < PageSize)
            {
                return null;
            }
        }
    }

    private async Task EnsureCustomerAsync(ApplicationUser user, string customerReference, CancellationToken ct)
    {
        if (await TryReadCustomerAsync(customerReference, ct) is not null)
        {
            return;
        }

        try
        {
            using (_postSendGate.BeginScope())
            {
                await _client.Customers.CreateCustomer(new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = user.FirstName!,
                        LastName = user.LastName!,
                        Email = user.Email!,
                        Reference = customerReference
                    }
                }, ct: ct);
            }
        }
        catch (SdkException<CreateCustomerError> exception) when (exception.Error.TryGetCustomerErrorResponse1(out _))
        {
            if (await TryReadCustomerAsync(customerReference, ct) is null)
            {
                throw ToProviderException(exception);
            }
        }
        catch (Exception exception) when (exception is MaxioPostRetryBlockedException || IsConnectivityFailure(exception))
        {
            if (await TryReadCustomerAsync(customerReference, ct) is null)
            {
                throw ProviderUnavailable(exception);
            }
        }
        catch (JsonException exception)
        {
            throw ProviderMalformed(exception);
        }
    }

    private async Task<Customer?> TryReadCustomerAsync(string customerReference, CancellationToken ct)
    {
        try
        {
            return (await _client.Customers.ReadCustomerByReference(customerReference, ct: ct)).Customer;
        }
        catch (SdkException<RawError> exception) when (exception.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> exception)
        {
            throw ToProviderException(exception);
        }
        catch (Exception exception) when (IsConnectivityFailure(exception))
        {
            throw ProviderUnavailable(exception);
        }
        catch (JsonException exception)
        {
            throw ProviderMalformed(exception);
        }
    }

    private async Task<Subscription?> TryFindSubscriptionAsync(string subscriptionReference, CancellationToken ct)
    {
        try
        {
            return (await _client.Subscriptions.FindSubscription(subscriptionReference, ct: ct)).Subscription;
        }
        catch (SdkException<FindSubscriptionError> exception) when (exception.Error.TryGetNoContent(out _))
        {
            return null;
        }
        catch (SdkException<FindSubscriptionError> exception)
        {
            throw ToProviderException(exception);
        }
        catch (Exception exception) when (IsConnectivityFailure(exception))
        {
            throw ProviderUnavailable(exception);
        }
        catch (JsonException exception)
        {
            throw ProviderMalformed(exception);
        }
    }

    private async Task<Subscription?> WaitForSubscriptionAsync(string reference, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            var subscription = await TryFindSubscriptionAsync(reference, ct);
            if (subscription is not null)
            {
                return subscription;
            }
        }

        return null;
    }

    private async Task<bool> TryClaimEnrollmentAsync(string userId, string planHandle, string reference, CancellationToken ct)
    {
        if (await _identityContext.SubscriptionEnrollments.AnyAsync(item => item.UserId == userId && item.PlanHandle == planHandle, ct))
        {
            return false;
        }

        _identityContext.SubscriptionEnrollments.Add(new SubscriptionEnrollment
        {
            UserId = userId,
            PlanHandle = planHandle,
            MaxioSubscriptionReference = reference,
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await _identityContext.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _identityContext.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task ConfirmEnrollmentAsync(string userId, string planHandle, int? maxioSubscriptionId, CancellationToken ct)
    {
        var enrollment = await _identityContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            item => item.UserId == userId && item.PlanHandle == planHandle, ct);
        if (enrollment is null)
        {
            return;
        }

        enrollment.MaxioSubscriptionId = maxioSubscriptionId;
        enrollment.ConfirmedAt = DateTimeOffset.UtcNow;
        await _identityContext.SaveChangesAsync(ct);
    }

    private static SubscriptionPlanDto ToPlanDto(Product product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value
    };

    private static SubscriptionDto ToSubscriptionDto(Subscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        State = subscription.State?.Value,
        NextBillingDate = subscription.NextAssessmentAt
    };

    private static string CustomerReference(string userId) => $"eshop-user-{Hash(userId)}";
    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-sub-{Hash($"{userId}:{planHandle}")}";

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool IsConnectivityFailure(Exception exception) => exception is HttpRequestException or TaskCanceledException;
    private static MaxioProviderException ProviderUnavailable(Exception exception) => new("The billing provider could not be reached. Retry the request shortly.", HttpStatusCode.BadGateway, true, exception);
    private static MaxioProviderException ProviderMalformed(Exception exception) => new("The billing provider returned a response that could not be processed.", HttpStatusCode.BadGateway, true, exception);
    private static MaxioProviderException ToProviderException(SdkException<RawError> exception) => new("The billing provider rejected the request.", exception.Error.StatusCode, innerException: exception);
    private static MaxioProviderException ToProviderException(SdkException<ListProductsForProductFamilyError> exception) => new("The billing provider rejected the request.", HttpStatusCode.BadGateway, innerException: exception);
    private static MaxioProviderException ToProviderException(SdkException<CreateCustomerError> exception) => new("The billing provider rejected the request.", HttpStatusCode.UnprocessableEntity, innerException: exception);
    private static MaxioProviderException ToProviderException(SdkException<FindSubscriptionError> exception) => new("The billing provider rejected the request.", HttpStatusCode.BadGateway, innerException: exception);
    private static MaxioProviderException ToProviderException(SdkException<CreateSubscriptionError> exception) => new("The billing provider rejected the request.", HttpStatusCode.UnprocessableEntity, innerException: exception);
}
