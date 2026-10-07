using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

/// <summary>
/// Maxio Advanced Billing–backed implementation of subscription billing.
/// Maxio is the system of record: the mapping from an eShopOnWeb user to
/// their Maxio customer and subscriptions is derived deterministically from
/// the user id (customer reference), so no local persistence is required and
/// the mapping survives process restarts.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    /// <summary>
    /// Name of the named HttpClient used for all Maxio traffic.
    /// </summary>
    public const string HttpClientName = "Maxio";

    /// <summary>
    /// Whole-call budget (across any SDK retries) for one Maxio operation.
    /// </summary>
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);

    private const int ProductsPerPage = 100;
    private const int MaxProductPages = 20;
    private const int MaxReferenceCandidates = 5;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IAppLogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        MaxioOptions options,
        IAppLogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var plans = new List<SubscriptionPlanDto>();

        await BoundedAsync(async token =>
        {
            for (var page = 1; page <= MaxProductPages; page++)
            {
                IReadOnlyList<ProductResponse> items;
                try
                {
                    items = await _client.Products.ListProducts(
                        dateField: null,
                        filter: null,
                        endDate: null,
                        endDatetime: null,
                        startDate: null,
                        startDatetime: null,
                        includeArchived: null,
                        include: null,
                        page: page,
                        perPage: ProductsPerPage,
                        ct: token);
                }
                catch (SdkException<RawError> ex)
                {
                    throw MapRawError("list the plans", ex.Error);
                }
                catch (JsonException ex)
                {
                    throw MapUnreadableResponse("listing the plans", ex);
                }

                foreach (var response in items)
                {
                    var product = response.Product;
                    if (product?.Handle is { Length: > 0 } &&
                        string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                    {
                        plans.Add(MapPlan(product));
                    }
                }

                if (items.Count < ProductsPerPage)
                {
                    break;
                }
            }

            return true;
        }, cancellationToken);

        return plans;
    }

    public async Task<SubscriptionDto> SubscribeAsync(UserBillingIdentity user, string planHandle, CancellationToken cancellationToken)
    {
        return await BoundedAsync(async token =>
        {
            var customer = await EnsureCustomerAsync(user, token);
            var product = await GetPlanProductAsync(planHandle, token);
            var collectionMethod = await GetPaymentCollectionMethodAsync(token);

            var baseReference = SubscriptionReference(user.UserId, planHandle);

            for (var candidate = 1; candidate <= MaxReferenceCandidates; candidate++)
            {
                var reference = candidate == 1 ? baseReference : $"{baseReference}-{candidate}";

                var existing = await FindSubscriptionByReferenceAsync(reference, token);
                if (existing is not null)
                {
                    // Idempotent replay: the user is already subscribed to
                    // this plan (e.g. a double-click) — return what Maxio has.
                    if (!IsTerminalState(existing))
                    {
                        _logger.LogInformation(
                            "Subscription {SubscriptionId} already exists for user {UserId} on plan {PlanHandle}; returning it.",
                            existing.Id, user.UserId, planHandle);
                        return MapSubscription(existing);
                    }

                    // A terminal (canceled/expired) subscription holds the
                    // reference; try the next candidate so the shopper can
                    // re-subscribe.
                    continue;
                }

                try
                {
                    var created = await _client.Subscriptions.CreateSubscription(
                        new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
                        {
                            Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
                            {
                                ProductHandle = product.Handle,
                                ProductPricePointId = product.ProductPricePointId,
                                CustomerId = customer.Id,
                                // No payment method at signup: an automatic
                                // collection method would attempt to charge the
                                // balance and fail without a card on file.
                                // Remittance (Relationship Invoicing sites) or
                                // Invoice (legacy sites) sign up without one.
                                PaymentCollectionMethod = collectionMethod,
                                Reference = reference
                            }
                        }, token);

                    return MapSubscription(created.Subscription!);
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var errors))
                    {
                        var joined = string.Join("; ", errors.Errors);

                        if (candidate < MaxReferenceCandidates &&
                            joined.Contains("reference", StringComparison.OrdinalIgnoreCase))
                        {
                            // The reference was taken between our lookup and
                            // the create; fall through to the next candidate.
                            continue;
                        }

                        throw new MaxioBillingException(422, $"Maxio rejected the subscription: {joined}");
                    }

                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw MapRawError("create the subscription", raw);
                    }

                    throw new MaxioBillingException(502, "Maxio returned an unrecognised error while creating the subscription.");
                }
                catch (JsonException ex)
                {
                    throw MapUnreadableResponse("creating the subscription", ex);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // Transport failure on a write — the outcome is unknown.
                    // Re-read Maxio state before reporting failure.
                    for (var retry = 1; retry <= candidate; retry++)
                    {
                        var candidateReference = retry == 1 ? baseReference : $"{baseReference}-{retry}";
                        var reconciled = await FindSubscriptionByReferenceAsync(candidateReference, token);
                        if (reconciled is not null && !IsTerminalState(reconciled))
                        {
                            _logger.LogInformation(
                                "Subscription create for user {UserId} on plan {PlanHandle} had an unknown outcome; reconcile found subscription {SubscriptionId}.",
                                user.UserId, planHandle, reconciled.Id);
                            return MapSubscription(reconciled);
                        }
                    }

                    throw new MaxioBillingException(503,
                        "The connection to Maxio failed while creating the subscription and its outcome is unknown. Please retry to confirm.");
                }
            }

            throw new MaxioBillingException(409,
                "Could not allocate a unique subscription reference in Maxio. Please try again.");
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForUserAsync(UserBillingIdentity user, CancellationToken cancellationToken)
    {
        return await BoundedAsync(async token =>
        {
            var reference = CustomerReference(user.UserId);

            Customer? customer;
            try
            {
                var response = await _client.Customers.ReadCustomerByReference(reference, token);
                customer = response.Customer;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // No Maxio customer yet — no subscriptions.
                return new List<SubscriptionDto>();
            }
            catch (SdkException<RawError> ex)
            {
                throw MapRawError("look up the customer's Maxio account", ex.Error);
            }
            catch (JsonException ex)
            {
                throw MapUnreadableResponse("looking up the customer's Maxio account", ex);
            }

            if (customer?.Id is not int customerId)
            {
                return new List<SubscriptionDto>();
            }

            IReadOnlyList<SubscriptionResponse> subscriptions;
            try
            {
                subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, token);
            }
            catch (SdkException<RawError> ex)
            {
                throw MapRawError("list the customer's subscriptions", ex.Error);
            }
            catch (JsonException ex)
            {
                throw MapUnreadableResponse("listing the customer's subscriptions", ex);
            }

            return subscriptions
                .Select(s => MapSubscription(s.Subscription!))
                .ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// Ensures a Maxio customer exists for the user. Idempotent: the customer
    /// is looked up by a reference derived from the user id, so concurrent or
    /// repeated calls never create two customers.
    /// </summary>
    private async Task<Customer> EnsureCustomerAsync(UserBillingIdentity user, CancellationToken token)
    {
        var reference = CustomerReference(user.UserId);

        Customer? existing = null;
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, token);
            existing = response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            existing = null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError("look up the customer's Maxio account", ex.Error);
        }
        catch (JsonException ex)
        {
            throw MapUnreadableResponse("looking up the customer's Maxio account", ex);
        }

        if (existing is not null)
        {
            return existing;
        }

        try
        {
            var created = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = user.FirstName,
                        LastName = user.LastName,
                        Email = user.Email,
                        Reference = reference
                    }
                }, token);

            _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}.", created.Customer?.Id, user.UserId);
            return created.Customer!;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new MaxioBillingException(422, "Maxio rejected the customer details as invalid.");
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("create the customer's Maxio account", raw);
            }

            throw new MaxioBillingException(502, "Maxio returned an unrecognised error while creating the customer account.");
        }
        catch (JsonException ex)
        {
            throw MapUnreadableResponse("creating the customer account", ex);
        }
    }

    /// <summary>
    /// The collection method to use for the no-card signup flow: Remittance
    /// on Relationship Invoicing sites, Invoice on legacy Statements
    /// Architecture sites.
    /// </summary>
    private async Task<CollectionMethod> GetPaymentCollectionMethodAsync(CancellationToken token)
    {
        try
        {
            var siteResponse = await _client.Sites.ReadSite(ct: token);
            if (siteResponse.Site?.RelationshipInvoicingEnabled == true)
            {
                return CollectionMethod.Remittance;
            }

            return CollectionMethod.Invoice;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError("read the Maxio site configuration", ex.Error);
        }
        catch (JsonException ex)
        {
            throw MapUnreadableResponse("reading the Maxio site configuration", ex);
        }
    }

    /// <summary>
    /// Loads the plan's Maxio product by handle and verifies it belongs to
    /// the configured product family.
    /// </summary>
    private async Task<Product> GetPlanProductAsync(string planHandle, CancellationToken token)
    {
        Product? product;
        try
        {
            var response = await _client.Products.ReadProductByHandle(planHandle, token);
            product = response.Product;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new MaxioBillingException(404, $"No subscription plan '{planHandle}' exists.");
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError("look up the plan", ex.Error);
        }
        catch (JsonException ex)
        {
            throw MapUnreadableResponse("looking up the plan", ex);
        }

        if (product is null ||
            !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw new MaxioBillingException(404, $"No subscription plan '{planHandle}' exists.");
        }

        return product;
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken token)
    {
        try
        {
            var response = await _client.Subscriptions.FindSubscription(reference, token);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex) when (ex.Error.TryGetNoContent(out _))
        {
            // 404 — no subscription holds this reference.
            return null;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError("look up an existing subscription", raw);
            }

            throw new MaxioBillingException(502, "Maxio returned an unrecognised error while looking up an existing subscription.");
        }
        catch (JsonException ex)
        {
            throw MapUnreadableResponse("looking up an existing subscription", ex);
        }
    }

    /// <summary>
    /// Bounds one Maxio operation with a total call budget (the only true
    /// whole-call bound — retry and HttpClient timeouts are per attempt), and
    /// converts connection failures into the billing error boundary.
    /// </summary>
    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TotalCallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Maxio became unreachable: {Message}", ex.Message);
            throw new MaxioBillingException(503, "Maxio is temporarily unreachable. Please try again.", ex);
        }
    }

    private MaxioBillingException MapRawError(string operation, RawError raw)
    {
        var status = (int)raw.StatusCode;
        var detail = SafeReadBody(raw);
        _logger.LogWarning("Maxio call to {Operation} failed with HTTP {Status}: {Detail}", operation, status, detail);

        var message = status switch
        {
            401 => "Maxio rejected the configured API credentials.",
            404 => $"Maxio reported a missing resource while attempting to {operation}.",
            422 => $"Maxio rejected the request while attempting to {operation}.",
            >= 500 => "Maxio is temporarily unavailable. Please try again.",
            _ => $"Maxio returned an unexpected response ({status}) while attempting to {operation}."
        };

        return new MaxioBillingException(status, message);
    }

    private MaxioBillingException MapUnreadableResponse(string operation, JsonException ex)
    {
        _logger.LogWarning("Maxio returned a response that could not be parsed while {Operation}: {Message}", operation, ex.Message);
        return new MaxioBillingException(502, "Maxio returned a response that could not be processed. Please try again.", ex);
    }

    private static string SafeReadBody(RawError raw)
    {
        try
        {
            return raw.ReadAsString() ?? "<empty body>";
        }
        catch
        {
            return "<unreadable body>";
        }
    }

    private static bool IsTerminalState(Subscription subscription) =>
        subscription.State?.Value is "canceled" or "expired";

    /// <summary>
    /// The Maxio customer reference for an eShopOnWeb user — the stable key
    /// that makes customer creation idempotent and lets subscriptions be
    /// resolved without local persistence.
    /// </summary>
    public static string CustomerReference(string userId) => $"eshopweb-user-{userId}";

    /// <summary>
    /// The Maxio subscription reference base for a user+plan — the stable key
    /// that makes subscribing idempotent per user and plan.
    /// </summary>
    public static string SubscriptionReference(string userId, string planHandle) =>
        $"{CustomerReference(userId)}-plan-{planHandle}";

    private static SubscriptionPlanDto MapPlan(Product product) => new()
    {
        Handle = product.Handle!,
        Name = product.Name,
        PriceInCents = product.PriceInCents ?? 0,
        Price = (product.PriceInCents ?? 0) / 100m,
        Interval = product.Interval ?? 1,
        IntervalUnit = product.IntervalUnit?.Value,
        ProductPricePointHandle = product.ProductPricePointHandle,
        RequireCreditCard = product.RequireCreditCard
    };

    private static SubscriptionDto MapSubscription(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id ?? 0,
        State = subscription.State?.Value,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        Price = (subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0) / 100m,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit?.Value,
        NextBillingDate = subscription.NextAssessmentAt,
        CurrentPeriodEndsOn = subscription.CurrentPeriodEndsAt
    };
}
