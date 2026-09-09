using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maxio Advanced Billing implementation of <see cref="IMaxioBillingService"/>.
/// All provider failures are translated here into <see cref="MaxioBillingException"/>
/// with a caller-safe message; provider responses are logged, never surfaced raw.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    public const string HttpClientName = "MaxioAdvancedBilling";

    private const int ProductsPerPage = 50;
    private const int MaxProductPages = 10;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // Cardless signup is rejected under automatic collection ("No payment method was on
    // file..."). Relationship Invoicing sites accept remittance; legacy Statements sites
    // accept invoice — try the current architecture first, then fall back.
    private static readonly CollectionMethod[] CollectionMethodCandidates =
    {
        CollectionMethod.Remittance,
        CollectionMethod.Invoice
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(IHttpClientFactory httpClientFactory,
        IOptions<MaxioOptions> options,
        ILogger<MaxioBillingService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var clientOptions = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            // Per-attempt timeout is bounded; the whole call is bounded by CallBudget
            // (see Bounded). Status retries keep the safe default of idempotent verbs only.
            Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = TimeSpan.FromSeconds(15) },
            BasicAuth = new BasicAuthCredentials { Username = _options.ApiKey, Password = "x" }
        };
        clientOptions.Server.Production.Us.Site = _options.Subdomain;
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            clientOptions.Server.Production.Us.BaseUrl = _options.BaseUrl;
        }

        _client = new MaxioAdvancedBillingClient(httpClientFactory.CreateClient(HttpClientName), clientOptions);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = await ListProductsInFamilyAsync(familyId, cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string email, string productHandle,
        CancellationToken cancellationToken)
    {
        var customer = await EnsureCustomerAsync(userId, email, cancellationToken);
        var customerId = customer.Id
            ?? throw new MaxioBillingException(502, "The billing provider returned a customer without an identifier.");

        // Idempotency: a subscription reference derived from (user, plan) means a
        // retried or double-clicked POST can never create a second subscription.
        var subscriptionReference = $"{userId}:{productHandle}";
        var existing = await FindSubscriptionOrNullAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return MapSubscription(existing)!;
        }

        for (var attempt = 0; attempt < CollectionMethodCandidates.Length; attempt++)
        {
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = CollectionMethodCandidates[attempt],
                    Reference = subscriptionReference
                }
            };

            try
            {
                var response = await Bounded(token => _client.Subscriptions.CreateSubscription(body: body, ct: token),
                    cancellationToken);
                return MapSubscription(response.Subscription)
                    ?? throw new MaxioBillingException(502, "The billing provider returned no subscription data.");
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    // 422 — could be a concurrent duplicate that slipped past the lookup;
                    // re-check before surfacing a rejection.
                    var raced = await FindSubscriptionOrNullAsync(subscriptionReference, cancellationToken);
                    if (raced is not null)
                    {
                        return MapSubscription(raced)!;
                    }

                    var errors = errorList.Errors ?? Array.Empty<string>();
                    if (attempt < CollectionMethodCandidates.Length - 1 &&
                        errors.Any(e => e.Contains("collection", StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInformation(
                            "Collection method {Method} rejected by the billing provider; trying the next candidate.",
                            CollectionMethodCandidates[attempt].Value);
                        continue;
                    }

                    _logger.LogWarning("Subscription creation rejected for user {UserId}, plan {PlanHandle}: {Errors}",
                        userId, productHandle, string.Join("; ", errors));
                    throw new MaxioBillingException(422,
                        $"The billing provider rejected the subscription to plan '{productHandle}'.");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRawError(raw, "subscription creation");
                }
                throw new MaxioBillingException(502, "The billing provider returned an unexpected error.");
            }
        }

        throw new MaxioBillingException(502, "The billing provider returned an unexpected error.");
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsForUserAsync(string userId,
        CancellationToken cancellationToken)
    {
        var customer = await FindCustomerByReferenceOrNullAsync(userId, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var customerId = customer.Id
            ?? throw new MaxioBillingException(502, "The billing provider returned a customer without an identifier.");

        var subscriptions = await Bounded(
            token => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: token), cancellationToken);
        return subscriptions.Where(s => s.Subscription is not null)
            .Select(s => MapSubscription(s.Subscription)!).ToList();
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        var families = await Bounded(
            token => _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: token),
            cancellationToken);

        var family = families.Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null && f.Handle == _options.ProductFamilyHandle);
        if (family is null)
        {
            _logger.LogWarning("Configured Maxio product family '{Handle}' was not found.", _options.ProductFamilyHandle);
            throw new MaxioBillingException(500, "The configured billing product family was not found.");
        }

        return family.Id
            ?? throw new MaxioBillingException(502, "The billing provider returned a product family without an identifier.");
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsInFamilyAsync(int productFamilyId,
        CancellationToken cancellationToken)
    {
        var products = new List<ProductResponse>();
        for (var page = 1; page <= MaxProductPages; page++)
        {
            var pageItems = await Bounded(
                token => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: productFamilyId.ToString(),
                    dateField: null, filter: null, startDate: null, endDate: null, startDatetime: null,
                    endDatetime: null, includeArchived: false, include: null,
                    page: page, perPage: ProductsPerPage, ct: token),
                cancellationToken);
            products.AddRange(pageItems);

            if (pageItems.Count < ProductsPerPage)
            {
                break;
            }

            if (page == MaxProductPages)
            {
                _logger.LogWarning("Product family {FamilyId} listing stopped at the {MaxPages}-page guard.",
                    productFamilyId, MaxProductPages);
            }
        }
        return products;
    }

    private async Task<Customer> EnsureCustomerAsync(string userId, string email, CancellationToken cancellationToken)
    {
        var existing = await FindCustomerByReferenceOrNullAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = DeriveNames(email);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            }
        };

        try
        {
            var response = await Bounded(token => _client.Customers.CreateCustomer(body: body, ct: token),
                cancellationToken);
            return response.Customer
                ?? throw new MaxioBillingException(502, "The billing provider returned no customer data.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422 — the reference is most likely taken by a concurrent create
                // (double-click race); the winner's record is the one to return.
                var raced = await FindCustomerByReferenceOrNullAsync(userId, cancellationToken);
                if (raced is not null)
                {
                    return raced;
                }
                _logger.LogWarning("Customer creation rejected for user {UserId}.", userId);
                throw new MaxioBillingException(422, "The billing provider rejected the customer creation.");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw MapRawError(raw, "customer creation");
            }
            throw new MaxioBillingException(502, "The billing provider returned an unexpected error.");
        }
    }

    private async Task<Customer?> FindCustomerByReferenceOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                token => _client.Customers.ReadCustomerByReference(reference: reference, ct: token), cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError(ex.Error, "customer lookup");
        }
    }

    private async Task<Subscription?> FindSubscriptionOrNullAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(token => _client.Subscriptions.FindSubscription(reference: reference, ct: token),
                cancellationToken);
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
                throw MapRawError(raw, "subscription lookup");
            }
            throw new MaxioBillingException(502, "The billing provider returned an unexpected error.");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MaxioBillingException(504, "The billing provider did not respond in time.");
        }
        catch (HttpRequestException)
        {
            throw new MaxioBillingException(503, "The billing provider is unreachable.");
        }
        catch (JsonException)
        {
            throw new MaxioBillingException(502, "The billing provider returned a response that could not be processed.");
        }
    }

    private MaxioBillingException MapRawError(RawError raw, string operation)
    {
        var providerStatus = (int)raw.StatusCode;
        string detail = "";
        try
        {
            detail = raw.ReadAsString() ?? "";
        }
        catch (Exception ex)
        {
            detail = $"(unreadable body: {ex.GetType().Name})";
        }
        _logger.LogWarning("Maxio returned {Status} during {Operation}: {Body}", providerStatus, operation,
            Truncate(detail, 500));

        var callerStatus = providerStatus switch
        {
            401 or 403 => 502, // our credentials/configuration, not the caller's fault
            >= 500 => 502,
            _ => providerStatus
        };
        var message = providerStatus switch
        {
            401 or 403 => "The billing provider rejected the configured credentials.",
            _ => $"The billing provider returned an unexpected error during {operation}."
        };
        return new MaxioBillingException(callerStatus, message);
    }

    private static SubscriptionPlanDto MapPlan(ProductResponse productResponse)
    {
        var product = productResponse.Product;
        return new SubscriptionPlanDto
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value,
            RequireCreditCard = product.RequireCreditCard,
            RequestCreditCard = product.RequestCreditCard
        };
    }

    private static SubscriptionDto? MapSubscription(Subscription? subscription)
    {
        if (subscription is null)
        {
            return null;
        }
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents,
            State = subscription.State?.Value,
            NextBillingDateUtc = subscription.CurrentPeriodEndsAt
        };
    }

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : "eShop";
        var lastName = parts.Length > 1 ? Capitalize(parts[^1]) : "Customer";
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "…";
}
