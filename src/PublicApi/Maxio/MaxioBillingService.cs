using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Fronts every Maxio Advanced Billing call made by the subscription capability. It owns the
/// find-or-create idempotency contracts (one customer per shopper, one subscription per shopper),
/// serializes the find-to-create critical section per shopper, applies a whole-call time budget, and
/// translates every SDK failure into a caller-safe <see cref="MaxioBillingException"/>. Details that
/// are safe to log but not to send to a client are written to the logger only.
/// </summary>
public class MaxioBillingService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PerUserGates = new();
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly MaxioWriteGuardHandler _writeGuard;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        MaxioWriteGuardHandler writeGuard,
        IHttpContextAccessor httpContextAccessor,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _writeGuard = writeGuard;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListSubscriptionPlansAsync()
    {
        var products = await GetFamilyProductsAsync();
        return products.Select(MapPlanDto).ToList();
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsForUserAsync(string email)
    {
        var reference = CustomerReference(email);
        var customer = await FindCustomerByReferenceAsync(reference);
        if (customer?.Id is null)
        {
            return new List<SubscriptionDto>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value);
        var result = new List<SubscriptionDto>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            result.Add(await MapSubscriptionAsync(subscription));
        }

        return result;
    }

    public async Task<SubscriptionEnrollmentResult> SubscribeAsync(string email, string planHandle, string firstName, string lastName)
    {
        var normalizedEmail = NormalizeEmail(email);
        var normalizedPlanHandle = planHandle?.Trim() ?? string.Empty;
        var normalizedFirstName = firstName?.Trim() ?? string.Empty;
        var normalizedLastName = lastName?.Trim() ?? string.Empty;

        if (normalizedEmail.Length == 0)
        {
            throw new MaxioBillingException(HttpStatusCode.BadRequest, "An authenticated e-mail address is required to subscribe.");
        }

        if (normalizedPlanHandle.Length == 0)
        {
            throw new MaxioBillingException(HttpStatusCode.BadRequest, "PlanHandle is required. Choose one of the plans returned by GET api/subscription-plans.");
        }

        if (normalizedFirstName.Length == 0 || normalizedLastName.Length == 0)
        {
            throw new MaxioBillingException(HttpStatusCode.BadRequest, "FirstName and LastName are required.");
        }

        var subscriptionReference = SubscriptionReference(normalizedEmail);
        var gate = PerUserGates.GetOrAdd(subscriptionReference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(RequestAborted);
        try
        {
            var existing = await FindSubscriptionByReferenceAsync(subscriptionReference);
            if (existing is not null)
            {
                var existingPlanHandle = existing.Product?.Handle;
                if (!string.IsNullOrWhiteSpace(existingPlanHandle) &&
                    !string.Equals(existingPlanHandle, normalizedPlanHandle, StringComparison.OrdinalIgnoreCase))
                {
                    throw new MaxioBillingException(HttpStatusCode.Conflict,
                        $"This shopper is already subscribed to plan '{existingPlanHandle}'. Switching or cancelling an existing subscription is not supported by this endpoint.");
                }

                return new SubscriptionEnrollmentResult(false, await MapSubscriptionAsync(existing));
            }

            var plan = await FindPlanByHandleAsync(normalizedPlanHandle);
            if (plan is null)
            {
                throw new MaxioBillingException(HttpStatusCode.NotFound,
                    $"Plan '{normalizedPlanHandle}' was not found in the configured Maxio product family.");
            }

            var customer = await FindOrCreateCustomerAsync(normalizedEmail, normalizedFirstName, normalizedLastName);
            var subscription = await CreateSubscriptionAndReconcileAsync(
                subscriptionReference, CustomerReference(normalizedEmail), plan, customer);

            return new SubscriptionEnrollmentResult(true, await MapSubscriptionAsync(subscription, plan));
        }
        finally
        {
            gate.Release();
        }
    }

    // ----- plan lookups ----------------------------------------------------

    private async Task<Product?> FindPlanByHandleAsync(string planHandle)
    {
        var products = await GetFamilyProductsAsync();
        return products.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<Product>> GetFamilyProductsAsync()
    {
        var familyHandle = _options.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(familyHandle))
        {
            throw new MaxioBillingException(HttpStatusCode.InternalServerError,
                $"{MaxioOptions.ConfigurationSectionName}:ProductFamilyHandle is not configured.");
        }

        try
        {
            var responses = await BoundedAsync("list plans for product family", ct =>
                _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + familyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: 1,
                    perPage: 50,
                    ct: ct));

            return responses
                .Where(r => r.Product is not null && r.Product.ArchivedAt is null)
                .Select(r => r.Product!)
                .ToList();
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _))
            {
                throw new MaxioBillingException(HttpStatusCode.InternalServerError,
                    $"The configured Maxio product family '{familyHandle}' was not found.");
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw UpstreamException("list plans for product family", raw);
            }

            throw UpstreamUnknownException("list plans for product family");
        }
    }

    // ----- customer find-or-create -----------------------------------------

    private async Task<Customer> FindOrCreateCustomerAsync(string email, string firstName, string lastName)
    {
        var reference = CustomerReference(email);

        var existing = await FindCustomerByReferenceAsync(reference);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var rejectedByServer = false;
        Customer? created = null;
        try
        {
            created = await TryCreateCustomerAsync(body);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                rejectedByServer = true;
            }
            else if (ex.Error.TryGetRawError(out var raw))
            {
                throw UpstreamException("create customer", raw);
            }
            else
            {
                throw UpstreamUnknownException("create customer");
            }
        }

        if (created is not null)
        {
            return created;
        }

        var winner = await FindCustomerByReferenceAsync(reference);
        if (winner is not null)
        {
            return winner;
        }

        if (rejectedByServer)
        {
            throw new MaxioBillingException(HttpStatusCode.BadRequest,
                "Maxio rejected the customer details for this shopper.");
        }

        throw new MaxioBillingException(HttpStatusCode.ServiceUnavailable,
            "The customer request was sent to Maxio but its outcome could not be confirmed.");
    }

    private async Task<Customer?> TryCreateCustomerAsync(CreateCustomerRequest body)
    {
        using var budget = CreateCallBudget();
        try
        {
            using var guard = _writeGuard.OpenScope();
            var response = await _client.Customers.CreateCustomer(body: body, ct: budget.Token);
            return response.Customer;
        }
        catch (MaxioWriteResendRefusedException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!RequestAborted.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var response = await BoundedAsync("find customer by reference", ct =>
                _client.Customers.ReadCustomerByReference(reference: reference, ct: ct));
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw UpstreamException("find customer by reference", ex.Error);
        }
    }

    // ----- subscription find / create --------------------------------------

    private async Task<Subscription> CreateSubscriptionAndReconcileAsync(string subscriptionReference, string customerReference, Product plan, Customer customer)
    {
        var planHandle = plan.Handle ?? string.Empty;
        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                Reference = subscriptionReference,
                CustomerId = customer.Id,
                CustomerReference = customer.Id is null ? customerReference : null,
                // This integration never captures a card. Maxio collects an immediate first bill at
                // signup unless one is deferred, and an immediate bill requires a chargeable payment
                // method even when the plan is configured not to require one. Deferring the first bill
                // to the plan's next natural interval is the documented cardless way to create the
                // subscription; the first collection then happens at that date instead of at signup.
                NextBillingAt = ComputeNextBillingAt(plan)
            }
        };

        Subscription? created;
        try
        {
            created = await TryCreateSubscriptionAsync(body);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorList))
            {
                var details = errorList.Errors is { Count: > 0 }
                    ? string.Join(" ", errorList.Errors)
                    : "The subscription request was rejected by Maxio.";
                throw new MaxioBillingException(HttpStatusCode.UnprocessableEntity, details);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw UpstreamException("create subscription", raw);
            }

            throw UpstreamUnknownException("create subscription");
        }

        if (created is not null)
        {
            return created;
        }

        var reconciled = await FindSubscriptionByReferenceAsync(subscriptionReference);
        if (reconciled is not null)
        {
            return reconciled;
        }

        throw new MaxioBillingException(HttpStatusCode.ServiceUnavailable,
            "The subscription request was sent to Maxio but its outcome could not be confirmed. Check your subscriptions before retrying.");
    }

    private async Task<Subscription?> TryCreateSubscriptionAsync(CreateSubscriptionRequest body)
    {
        using var budget = CreateCallBudget();
        try
        {
            using var guard = _writeGuard.OpenScope();
            var response = await _client.Subscriptions.CreateSubscription(body: body, ct: budget.Token);
            return response.Subscription;
        }
        catch (MaxioWriteResendRefusedException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!RequestAborted.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference)
    {
        try
        {
            var response = await BoundedAsync("find subscription by reference", ct =>
                _client.Subscriptions.FindSubscription(reference: reference, ct: ct));
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
                throw UpstreamException("find subscription by reference", raw);
            }

            throw UpstreamUnknownException("find subscription by reference");
        }
    }

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var responses = await BoundedAsync("list customer subscriptions", ct =>
                _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct));
            return responses
                .Where(r => r.Subscription is not null)
                .Select(r => r.Subscription!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw UpstreamException("list customer subscriptions", ex.Error);
        }
    }

    private async Task<Subscription?> ReadSubscriptionAsync(int subscriptionId)
    {
        try
        {
            var response = await BoundedAsync("read subscription", ct =>
                _client.Subscriptions.ReadSubscription(subscriptionId: subscriptionId, include: null, ct: ct));
            return response.Subscription;
        }
        catch (SdkException<RawError> ex)
        {
            throw UpstreamException("read subscription", ex.Error);
        }
    }

    // ----- DTO mapping ------------------------------------------------------

    private SubscriptionPlanDto MapPlanDto(Product product)
    {
        return new SubscriptionPlanDto
        {
            PlanHandle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Price = CentsToPrice(product.PriceInCents) ?? 0m,
            IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
            Interval = product.Interval,
            RequiresCreditCard = product.RequireCreditCard ?? false
        };
    }

    private async Task<SubscriptionDto> MapSubscriptionAsync(Subscription subscription, Product? fallbackPlan = null)
    {
        var product = subscription.Product ?? fallbackPlan;

        // The product embedded in subscription payloads is populated on most operations but not
        // guaranteed on all of them; re-read a subscription that carries none so plan details survive.
        if (product is null && subscription.Id is not null)
        {
            var refreshed = await ReadSubscriptionAsync(subscription.Id.Value);
            product = refreshed?.Product;
        }

        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            Price = CentsToPrice(product?.PriceInCents ?? subscription.ProductPriceInCents),
            State = subscription.State?.Value ?? string.Empty,
            CurrentPeriodStart = subscription.CurrentPeriodStartedAt,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private static decimal? CentsToPrice(long? cents) => cents is null ? null : cents.Value / 100m;

    /// <summary>
    /// The date the plan's next collection should occur. When the plan bills monthly the first bill is
    /// deferred by <paramref name="plan"/>'s interval in months (or days for a daily plan).
    /// </summary>
    private static DateTimeOffset ComputeNextBillingAt(Product plan)
    {
        var interval = Math.Max(1, plan.Interval ?? 1);
        var now = DateTimeOffset.UtcNow;
        return string.Equals(plan.IntervalUnit?.Value, "day", StringComparison.OrdinalIgnoreCase)
            ? now.AddDays(interval)
            : now.AddMonths(interval);
    }

    // ----- SDK call scaffolding ---------------------------------------------

    private CancellationTokenSource CreateCallBudget()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(RequestAborted);
        cts.CancelAfter(CallTimeout);
        return cts;
    }

    private CancellationToken RequestAborted => _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;

    private async Task<T> BoundedAsync<T>(string operation, Func<CancellationToken, Task<T>> call)
    {
        using var budget = CreateCallBudget();
        try
        {
            return await call(budget.Token);
        }
        catch (HttpRequestException ex)
        {
            throw UpstreamException(operation, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The Maxio billing service returned a response that could not be processed.", ex);
        }
        catch (OperationCanceledException) when (!RequestAborted.IsCancellationRequested)
        {
            throw new MaxioBillingException(HttpStatusCode.GatewayTimeout,
                "The Maxio billing request timed out.");
        }
    }

    private MaxioBillingException UpstreamException(string operation, RawError raw)
    {
        _logger.LogError("Maxio request '{Operation}' failed with HTTP {StatusCode}. Body: {Body}",
            operation, (int)raw.StatusCode, raw.ReadAsString());
        return UpstreamUnavailable(operation);
    }

    private MaxioBillingException UpstreamException(string operation, HttpRequestException exception)
    {
        _logger.LogError(exception, "Maxio request '{Operation}' failed at the transport level.", operation);
        return UpstreamUnavailable(operation);
    }

    private MaxioBillingException UpstreamUnknownException(string operation)
    {
        _logger.LogError("Maxio request '{Operation}' failed with an unrecognized error response.", operation);
        return UpstreamUnavailable(operation);
    }

    private static MaxioBillingException UpstreamUnavailable(string operation)
    {
        return new MaxioBillingException(HttpStatusCode.ServiceUnavailable,
            $"The Maxio billing service could not complete the '{operation}' request.");
    }

    // ----- identity helpers -------------------------------------------------

    private static string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string CustomerReference(string email) => "eshop-customer-" + HashIdentity(email);

    private static string SubscriptionReference(string email) => "eshop-subscription-" + HashIdentity(email);

    private static string HashIdentity(string normalizedEmail)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
