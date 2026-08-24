using System;
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
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan EnrollmentLease = TimeSpan.FromMinutes(2);
    private const int ProductPageSize = 100;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly MaxioRequestContext _requestContext;
    private readonly SubscriptionOperationLock _operationLock;
    private readonly CatalogContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionBillingService> _logger;

    public SubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        MaxioRequestContext requestContext,
        SubscriptionOperationLock operationLock,
        CatalogContext dbContext,
        UserManager<ApplicationUser> userManager,
        ILogger<SubscriptionBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _requestContext = requestContext;
        _operationLock = operationLock;
        _dbContext = dbContext;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = new List<Product>();

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> response;
            using var scope = _requestContext.Begin();
            try
            {
                response = await BoundedAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw TranslateListProductsError(ex);
            }
            catch (JsonException ex)
            {
                throw TranslateJsonError(scope.LastStatusCode, ex);
            }
            catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
            {
                throw ProviderUnavailable(ex);
            }

            products.AddRange(response.Select(x => x.Product).Where(x => x.ArchivedAt is null));
            if (response.Count < ProductPageSize)
            {
                break;
            }
        }

        return products
            .Where(x => !string.IsNullOrWhiteSpace(x.Handle))
            .Select(MapPlan)
            .ToArray();
    }

    public async Task<CreateSubscriptionResponse> SubscribeAsync(
        ClaimsPrincipal principal,
        string productHandle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new SubscriptionBillingException(HttpStatusCode.BadRequest, "A productHandle is required.");
        }

        var user = await ResolveUserAsync(principal);
        var product = await ReadEligibleProductAsync(productHandle.Trim(), cancellationToken);
        var canonicalHandle = product.Handle!;
        var customerReference = ReferenceFor("eshop-u-", user.Id);
        var subscriptionReference = ReferenceFor(
            "eshop-s-",
            user.Id + "\0" + canonicalHandle.ToUpperInvariant());

        using var operationLock = await _operationLock.AcquireAsync(subscriptionReference, cancellationToken);
        var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);
        var customerId = customer.Id ?? throw MalformedProviderResponse();

        var (enrollment, ownsClaim) = await AcquireEnrollmentClaimAsync(
            user.Id,
            canonicalHandle,
            subscriptionReference,
            cancellationToken);

        var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            await CompleteEnrollmentAsync(enrollment, customerId, RequireSubscriptionId(existing), cancellationToken);
            return new CreateSubscriptionResponse { Created = false, Subscription = MapSubscription(existing) };
        }

        if (enrollment.IsCompleted)
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.Conflict,
                "The local enrollment exists but Maxio could not find the subscription. Contact support before retrying.");
        }

        if (!ownsClaim)
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.Conflict,
                "This subscription enrollment is already in progress. Retry shortly.");
        }

        try
        {
            var created = await CreateSubscriptionAsync(
                canonicalHandle,
                customerReference,
                subscriptionReference,
                cancellationToken);
            await CompleteEnrollmentAsync(enrollment, customerId, RequireSubscriptionId(created), cancellationToken);
            return new CreateSubscriptionResponse { Created = true, Subscription = MapSubscription(created) };
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex))
        {
            _logger.LogWarning(ex, "Maxio subscription create had an ambiguous outcome; reconciling by reference");
            var reconciled = await ReconcileSubscriptionAsync(subscriptionReference);
            if (reconciled is not null)
            {
                await CompleteEnrollmentAsync(enrollment, customerId, RequireSubscriptionId(reconciled), CancellationToken.None);
                return new CreateSubscriptionResponse { Created = true, Subscription = MapSubscription(reconciled) };
            }

            enrollment.ReleaseLease();
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw new SubscriptionBillingException(
                HttpStatusCode.ServiceUnavailable,
                "The subscription outcome could not be confirmed. It is safe to retry.",
                ex);
        }
        catch
        {
            enrollment.ReleaseLease();
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var customerReference = ReferenceFor("eshop-u-", user.Id);
        var customer = await ReadCustomerAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var customerId = customer.Id ?? throw MalformedProviderResponse();
        IReadOnlyList<SubscriptionResponse> response;
        using var scope = _requestContext.Begin();
        try
        {
            response = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "Maxio could not list subscriptions.", ex);
        }
        catch (JsonException ex)
        {
            throw TranslateJsonError(scope.LastStatusCode, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnavailable(ex);
        }

        return response
            .Where(x => x.Subscription is not null)
            .Select(x => MapSubscription(x.Subscription!))
            .ToArray();
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> response;
        using var scope = _requestContext.Begin();
        try
        {
            response = await BoundedAsync(
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
            throw TranslateRawError(ex.Error, "Maxio could not load product families.", ex);
        }
        catch (JsonException ex)
        {
            throw TranslateJsonError(scope.LastStatusCode, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnavailable(ex);
        }

        var family = response
            .Select(x => x.ProductFamily)
            .SingleOrDefault(x => string.Equals(
                x?.Handle,
                _options.ProductFamilyHandle,
                StringComparison.OrdinalIgnoreCase));

        if (family?.Id is not int id)
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.ServiceUnavailable,
                "The configured Maxio product family is unavailable.");
        }

        return id;
    }

    private async Task<Product> ReadEligibleProductAsync(
        string productHandle,
        CancellationToken cancellationToken)
    {
        ProductResponse response;
        using var scope = _requestContext.Begin();
        try
        {
            response = await BoundedAsync(
                ct => _client.Products.ReadProductByHandle(productHandle, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new SubscriptionBillingException(HttpStatusCode.NotFound, "The requested subscription plan was not found.", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "Maxio could not load the requested plan.", ex);
        }
        catch (JsonException ex)
        {
            throw TranslateJsonError(scope.LastStatusCode, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnavailable(ex);
        }

        var product = response.Product;
        if (!string.Equals(
                product.ProductFamily?.Handle,
                _options.ProductFamilyHandle,
                StringComparison.OrdinalIgnoreCase)
            || product.ArchivedAt is not null)
        {
            throw new SubscriptionBillingException(HttpStatusCode.NotFound, "The requested subscription plan was not found.");
        }

        if (string.IsNullOrWhiteSpace(product.Handle))
        {
            throw MalformedProviderResponse();
        }

        if (product.RequireCreditCard == true)
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.Conflict,
                "This plan requires a payment method and cannot be enrolled through this endpoint.");
        }

        return product;
    }

    private async Task<Customer> EnsureCustomerAsync(
        ApplicationUser user,
        string reference,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerAsync(reference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new SubscriptionBillingException(
                HttpStatusCode.UnprocessableEntity,
                "The authenticated account needs an email address before it can subscribe.");
        }

        var (firstName, lastName) = NamesFromEmail(email);
        var request = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        using var scope = _requestContext.Begin();
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.CreateCustomer(request, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var reconciled = await ReadCustomerAsync(reference, cancellationToken);
                if (reconciled is not null)
                {
                    return reconciled;
                }

                throw new SubscriptionBillingException(
                    HttpStatusCode.UnprocessableEntity,
                    "Maxio rejected the customer profile.",
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "Maxio could not create the customer.", ex);
            }

            throw ProviderUnavailable(ex);
        }
        catch (JsonException ex)
        {
            throw TranslateJsonError(scope.LastStatusCode, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
        {
            var reconciled = await ReconcileCustomerAsync(reference);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw ProviderUnavailable(ex);
        }
    }

    private async Task<Customer?> ReadCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var scope = _requestContext.Begin();
        try
        {
            var response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, "Maxio could not load the customer.", ex);
        }
        catch (JsonException ex)
        {
            throw TranslateJsonError(scope.LastStatusCode, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnavailable(ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        using var scope = _requestContext.Begin();
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.FindSubscription(reference, ct: ct),
                cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out var notFound)
                && notFound.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "Maxio could not find the subscription.", ex);
            }

            throw ProviderUnavailable(ex);
        }
        catch (JsonException ex)
        {
            throw TranslateJsonError(scope.LastStatusCode, ex);
        }
        catch (Exception ex) when (IsProviderTransportFailure(ex, cancellationToken))
        {
            throw ProviderUnavailable(ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var request = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = CollectionMethod.Remittance
            }
        };

        using var scope = _requestContext.Begin(guardPostResends: true);
        try
        {
            var response = await BoundedAsync(
                ct => _client.Subscriptions.CreateSubscription(request, ct: ct),
                cancellationToken);
            return response.Subscription ?? throw MalformedProviderResponse();
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var rejection))
            {
                _logger.LogWarning(
                    "Maxio rejected subscription creation for product {ProductHandle}. Validation errors: {ValidationErrors}",
                    productHandle,
                    FormatValidationErrorsForLog(rejection.Errors));
                throw new SubscriptionBillingException(
                    HttpStatusCode.UnprocessableEntity,
                    "Maxio rejected the subscription request.",
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRawError(raw, "Maxio could not create the subscription.", ex);
            }

            throw ProviderUnavailable(ex);
        }
        catch (JsonException ex)
        {
            if (scope.LastStatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
            {
                throw new SubscriptionBillingException(
                    scope.LastStatusCode.Value,
                    "Maxio rejected the subscription request.",
                    ex);
            }

            throw;
        }
    }

    private async Task<(SubscriptionEnrollment Enrollment, bool OwnsClaim)> AcquireEnrollmentClaimAsync(
        string userId,
        string productHandle,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var normalizedHandle = productHandle.ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var enrollment = await _dbContext.SubscriptionEnrollments.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ProductHandle == normalizedHandle,
            cancellationToken);

        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment(
                userId,
                normalizedHandle,
                subscriptionReference,
                now.Add(EnrollmentLease));
            _dbContext.SubscriptionEnrollments.Add(enrollment);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return (enrollment, true);
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(enrollment).State = EntityState.Detached;
                enrollment = await _dbContext.SubscriptionEnrollments.SingleAsync(
                    x => x.UserId == userId && x.ProductHandle == normalizedHandle,
                    cancellationToken);
            }
        }

        if (enrollment.IsCompleted)
        {
            return (enrollment, false);
        }

        if (enrollment.LeaseExpiresAt > now)
        {
            return (enrollment, false);
        }

        enrollment.AcquireLease(now.Add(EnrollmentLease));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (enrollment, true);
    }

    private async Task CompleteEnrollmentAsync(
        SubscriptionEnrollment enrollment,
        int customerId,
        int subscriptionId,
        CancellationToken cancellationToken)
    {
        enrollment.Complete(customerId, subscriptionId);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Customer?> ReconcileCustomerAsync(string reference)
    {
        try
        {
            return await ReadCustomerAsync(reference, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Maxio customer reconciliation failed");
            return null;
        }
    }

    private async Task<Subscription?> ReconcileSubscriptionAsync(string reference)
    {
        try
        {
            return await FindSubscriptionAsync(reference, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Maxio subscription reconciliation failed");
            return null;
        }
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var userById = await _userManager.FindByIdAsync(userId);
            if (userById is not null)
            {
                return userById;
            }
        }

        var userName = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionBillingException(HttpStatusCode.Unauthorized, "The bearer token has no user identity.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        return user ?? throw new SubscriptionBillingException(HttpStatusCode.Unauthorized, "The authenticated account no longer exists.");
    }

    private static async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await operation(cts.Token);
    }

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        ProductHandle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value,
        RequestsCreditCard = product.RequestCreditCard == true,
        RequiresCreditCard = product.RequireCreditCard == true
    };

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        if (subscription.Id is not int id)
        {
            throw MalformedProviderResponse();
        }

        return new SubscriptionDto
        {
            Id = id,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? subscription.Product?.Handle ?? "Subscription",
            PriceInCents = subscription.ProductPriceInCents,
            CurrentBillingAmountInCents = subscription.CurrentBillingAmountInCents,
            Currency = subscription.Currency,
            State = subscription.State?.Value,
            NextBillingDate = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
    }

    private static int RequireSubscriptionId(Subscription subscription) =>
        subscription.Id ?? throw MalformedProviderResponse();

    private static string ReferenceFor(string prefix, string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return prefix + Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static (string FirstName, string LastName) NamesFromEmail(string email)
    {
        var localPart = email.Split('@', 2)[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : "eShop";
        var lastName = parts.Length > 1 ? parts[^1] : "Customer";
        return (firstName, lastName);
    }

    private static string FormatValidationErrorsForLog(IReadOnlyList<string> errors)
    {
        const int maxErrors = 10;
        const int maxErrorLength = 500;
        var safeErrors = errors
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(maxErrors)
            .Select(x =>
            {
                var singleLine = x.ReplaceLineEndings(" ");
                return singleLine.Length <= maxErrorLength
                    ? singleLine
                    : singleLine[..maxErrorLength] + "…";
            });
        return string.Join(" | ", safeErrors);
    }

    private static SubscriptionBillingException TranslateListProductsError(
        SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out _))
        {
            return new SubscriptionBillingException(HttpStatusCode.NotFound, "The configured Maxio product family was not found.", ex);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw, "Maxio could not load subscription plans.", ex);
        }

        return ProviderUnavailable(ex);
    }

    private static SubscriptionBillingException TranslateRawError(
        RawError raw,
        string message,
        Exception innerException)
    {
        var status = raw.StatusCode;
        var publicStatus = status >= HttpStatusCode.BadRequest && status < HttpStatusCode.InternalServerError
            ? status
            : HttpStatusCode.BadGateway;
        return new SubscriptionBillingException(publicStatus, message, innerException);
    }

    private static SubscriptionBillingException TranslateJsonError(HttpStatusCode? status, JsonException exception)
    {
        if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
        {
            return new SubscriptionBillingException(status.Value, "Maxio rejected the request.", exception);
        }

        return new SubscriptionBillingException(
            HttpStatusCode.BadGateway,
            "Maxio returned a response that could not be processed.",
            exception);
    }

    private static bool IsProviderTransportFailure(Exception exception, CancellationToken callerToken)
    {
        if (callerToken.IsCancellationRequested)
        {
            return false;
        }

        return exception is HttpRequestException
            or TaskCanceledException
            or TimeoutRejectedException;
    }

    private static bool IsAmbiguousWriteFailure(Exception exception) =>
        exception is MaxioWriteResendBlockedException
        or HttpRequestException
        or TaskCanceledException
        or TimeoutRejectedException
        or JsonException
        || exception is SubscriptionBillingException billingException
            && (int)billingException.StatusCode >= 500;

    private static SubscriptionBillingException ProviderUnavailable(Exception innerException) => new(
        HttpStatusCode.ServiceUnavailable,
        "Maxio is temporarily unavailable.",
        innerException);

    private static SubscriptionBillingException MalformedProviderResponse() => new(
        HttpStatusCode.BadGateway,
        "Maxio returned an incomplete response.");
}
