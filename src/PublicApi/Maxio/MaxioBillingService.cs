using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using CreateSubscriptionSdkRequest = MaxioAdvancedBilling.Models.CreateSubscriptionRequest;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioBillingService : IMaxioBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int ProductsPageSize = 100;
    private const int MaxProductPages = 20;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscriptionLocks = new();

    public MaxioBillingService(MaxioAdvancedBillingClient client, MaxioOptions options, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var family = await FindProductFamilyAsync(cancellationToken);
        var products = await ListFamilyProductsAsync(family.Id!.Value, cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new MaxioBillingException(HttpStatusCode.NotFound, $"Subscription plan '{planHandle}' was not found.");
        }

        var reference = SubscriptionReference(userId, plan.Handle);
        var gate = _subscriptionLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
            if (existing is not null)
            {
                return new SubscribeResult(MapSubscription(existing), Created: false);
            }

            await EnsureCustomerAsync(userId, email, cancellationToken);

            var subscription = await CreateSubscriptionAsync(plan.Handle, userId, reference, cancellationToken);
            _logger.LogInformation("Subscribed user {UserId} to plan {PlanHandle} (Maxio subscription {SubscriptionId})", userId, plan.Handle, subscription.Id);
            return new SubscribeResult(MapSubscription(subscription), Created: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var customer = await FindCustomerByReferenceAsync(userId, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        try
        {
            var responses = await Bounded(token => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: token), cancellationToken);
            return responses
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error.StatusCode, SafeBody(ex.Error), "listing subscriptions", ex);
        }
    }

    public async Task<SubscriptionDto> CancelSubscriptionAsync(string userId, int subscriptionId, CancellationToken cancellationToken = default)
    {
        Subscription? subscription;
        try
        {
            var response = await Bounded(token => _client.Subscriptions.ReadSubscription(
                subscriptionId, include: null, ct: token), cancellationToken);
            subscription = response.Subscription;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error.StatusCode, SafeBody(ex.Error), "reading the subscription", ex);
        }

        if (subscription is null || !string.Equals(subscription.Customer?.Reference, userId, StringComparison.Ordinal))
        {
            throw new MaxioBillingException(HttpStatusCode.NotFound, "Subscription not found for the current user.");
        }

        try
        {
            var response = await Bounded(token => _client.SubscriptionStatus.CancelSubscription(
                subscriptionId, body: null, ct: token), cancellationToken);
            return MapSubscription(response.Subscription!);
        }
        catch (SdkException<CancelSubscriptionApiError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                throw new MaxioBillingException(HttpStatusCode.NotFound, "Subscription not found.", ex);
            }

            if (ex.Error.TryGetCancelSubscriptionErrorResponse(out var cancelError))
            {
                var detail = cancelError.TryGetErrorListResponse1(out var list) ? string.Join("; ", list.Errors)
                    : cancelError.TryGetSingleErrorResponse1(out var single) ? single.Error
                    : string.Empty;
                throw new MaxioBillingException(HttpStatusCode.UnprocessableEntity, $"Unable to cancel the subscription. {detail}".Trim(), ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw.StatusCode, SafeBody(raw), "cancelling the subscription", ex);
            }

            throw new MaxioBillingException(HttpStatusCode.BadGateway, "The billing provider returned an unexpected error while cancelling the subscription.", ex);
        }
    }

    private async Task<ProductFamily> FindProductFamilyAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await Bounded(token => _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: token), cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error.StatusCode, SafeBody(ex.Error), "listing product families", ex);
        }

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => string.Equals(f?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new MaxioBillingException(
                HttpStatusCode.InternalServerError,
                $"The configured subscription catalog ('{_options.ProductFamilyHandle}') was not found in the billing system.");
        }

        return family;
    }

    private async Task<List<Product>> ListFamilyProductsAsync(int productFamilyId, CancellationToken cancellationToken)
    {
        var result = new List<Product>();
        for (var page = 1; page <= MaxProductPages; page++)
        {
            IReadOnlyList<ProductResponse> products;
            try
            {
                products = await Bounded(token => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: productFamilyId.ToString(CultureInfo.InvariantCulture),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: ProductsPageSize,
                    ct: token), cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var notFound))
                {
                    throw new MaxioBillingException(HttpStatusCode.NotFound, $"Subscription catalog not found in the billing system. {Truncate(notFound)}", ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRawError(raw.StatusCode, SafeBody(raw), "listing subscription plans", ex);
                }

                throw new MaxioBillingException(HttpStatusCode.BadGateway, "The billing provider returned an unexpected error while listing subscription plans.", ex);
            }

            result.AddRange(products
                .Select(p => p.Product)
                .Where(p => p is not null && p.ArchivedAt is null)
                .Select(p => p!));

            if (products.Count < ProductsPageSize)
            {
                break;
            }
        }

        return result;
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(token => _client.Customers.ReadCustomerByReference(userId, ct: token), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            _logger.LogError(ex, "Maxio customer lookup failed for user {UserId}", userId);
            throw MapRawError(ex.Error.StatusCode, SafeBody(ex.Error), "looking up the billing customer", ex);
        }
    }

    private async Task<Customer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var response = await Bounded(token => _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    Reference = userId,
                    FirstName = "eShopOnWeb",
                    LastName = "Customer",
                    Email = email
                }
            }, ct: token), cancellationToken);
            return response.Customer!;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var status = HttpStatusCode.BadGateway;
            var detail = string.Empty;
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                status = HttpStatusCode.UnprocessableEntity;
                detail = "The billing provider rejected the customer details.";
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                status = raw.StatusCode;
                detail = TryExtractErrorDetail(SafeBody(raw));
            }

            if (status == HttpStatusCode.UnprocessableEntity)
            {
                var raced = await FindCustomerByReferenceAsync(userId, cancellationToken);
                if (raced is not null)
                {
                    return raced;
                }
            }

            _logger.LogError(ex, "Maxio customer creation failed for user {UserId} with status {StatusCode}", userId, (int)status);
            throw new MaxioBillingException(MapStatus(status), $"Unable to create the billing customer ({(int)status}). {detail}".Trim(), ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(token => _client.Subscriptions.FindSubscription(reference, ct: token), cancellationToken);
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

                throw MapRawError(raw.StatusCode, SafeBody(raw), "looking up an existing subscription", ex);
            }

            throw new MaxioBillingException(HttpStatusCode.BadGateway, "The billing provider returned an unexpected error while looking up an existing subscription.", ex);
        }
    }

    private async Task<Subscription> CreateSubscriptionAsync(string planHandle, string userId, string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(token => _client.Subscriptions.CreateSubscription(new CreateSubscriptionSdkRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerReference = userId,
                    Reference = reference,
                    // The demo plans require no card capture (no 3-DS): remittance
                    // enrollment lets Maxio create the subscription without a
                    // payment profile on file.
                    PaymentCollectionMethod = CollectionMethod.Remittance
                }
            }, ct: token), cancellationToken);
            return response.Subscription!;
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var status = HttpStatusCode.BadGateway;
            var detail = string.Empty;
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                status = HttpStatusCode.UnprocessableEntity;
                detail = string.Join("; ", errors.Errors);
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                status = raw.StatusCode;
                detail = TryExtractErrorDetail(SafeBody(raw));
            }

            if (status == HttpStatusCode.UnprocessableEntity)
            {
                var raced = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (raced is not null)
                {
                    return raced;
                }
            }

            _logger.LogError(ex, "Maxio subscription creation failed for user {UserId}, plan {PlanHandle}, status {StatusCode}", userId, planHandle, (int)status);
            throw new MaxioBillingException(MapStatus(status), $"Unable to create the subscription ({(int)status}). {detail}".Trim(), ex);
        }
    }

    private static string SubscriptionReference(string userId, string planHandle) => $"eshop-{userId}-{planHandle}";

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (JsonException ex)
        {
            throw new MaxioBillingException(HttpStatusCode.BadGateway, "The billing provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MaxioBillingException(HttpStatusCode.BadGateway, "The billing provider could not be reached.", ex);
        }
    }

    private MaxioBillingException MapRawError(HttpStatusCode status, string body, string operation, Exception inner)
    {
        var detail = TryExtractErrorDetail(body);
        var message = $"The billing provider failed while {operation} ({(int)status}). {detail}".Trim();
        return new MaxioBillingException(MapStatus(status), message, inner);
    }

    private static HttpStatusCode MapStatus(HttpStatusCode providerStatus) =>
        (int)providerStatus is >= 400 and < 500 ? providerStatus : HttpStatusCode.BadGateway;

    private static string SafeBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryExtractErrorDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList();
                if (messages.Count > 0)
                {
                    return Truncate(string.Join("; ", messages!));
                }
            }
        }
        catch (JsonException)
        {
        }

        return Truncate(body);
    }

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300] + "…";

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        Price = (product.PriceInCents ?? 0) / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit?.Value
    };

    private static SubscriptionDto MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id ?? 0,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        Price = (subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0) / 100m,
        State = subscription.State?.Value ?? string.Empty,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        Reference = subscription.Reference
    };
}
