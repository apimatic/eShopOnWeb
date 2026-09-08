using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

// Alias to disambiguate from Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.CreateSubscriptionRequest.
using MaxioCreateSubscriptionRequest = MaxioAdvancedBilling.Models.CreateSubscriptionRequest;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maxio Advanced Billing gateway for the subscription feature. Translates every
/// SDK error (typed API errors, raw provider errors, transport failures, malformed
/// bodies and retry-refusal sentinels) into <see cref="MaxioSubscriptionException"/>
/// so endpoints never see SDK types.
/// </summary>
public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    /// <summary>The seeded default plan handle used to flag the default plan in browse results.</summary>
    internal const string DefaultPlanHandle = "eshop-pro";

    private const string SiteCurrencyCacheKey = "maxio:site-currency";
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan CurrencyCacheTtl = TimeSpan.FromMinutes(15);

    private static readonly HashSet<string> TerminalStates =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "canceled", "expired" };

    private static readonly HashSet<string> ActiveStates =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "active", "trialing" };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MaxioSubscriptionService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _subscriptionLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        MaxioOptions options,
        IMemoryCache cache,
        ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken ct)
    {
        var currency = await GetSiteCurrencyOrNullAsync(ct);

        IReadOnlyList<ProductResponse> responses;
        try
        {
            responses = await ExecuteWithBudgetAsync(
                inner => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + _options.ProductFamilyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: 1,
                    perPage: 20,
                    ct: inner),
                ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw TranslateListProductsError(ex);
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransport(ex, "list subscription plans");
        }
        catch (JsonException ex)
        {
            throw TranslateUnreadableBody(ex);
        }

        var plans = new List<SubscriptionPlanDto>();
        foreach (var response in responses)
        {
            var product = response.Product;
            if (product is null || product.ArchivedAt is not null)
            {
                continue;
            }

            plans.Add(new SubscriptionPlanDto
            {
                Handle = product.Handle ?? string.Empty,
                Name = product.Name ?? string.Empty,
                Price = ToMajorUnits(product.PriceInCents),
                Currency = currency,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit?.Value ?? "month",
                IsDefault = string.Equals(product.Handle, DefaultPlanHandle, StringComparison.Ordinal),
                PaymentRequired = product.RequireCreditCard == true
            });
        }

        return plans;
    }

    public async Task<SubscribeToPlanResult> SubscribeAsync(SubscribeToPlanRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerReference))
        {
            throw new MaxioSubscriptionException(StatusCodes.Status400BadRequest, "A customer reference is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new MaxioSubscriptionException(StatusCodes.Status400BadRequest, "A planHandle is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new MaxioSubscriptionException(StatusCodes.Status400BadRequest, "A valid account email is required to subscribe.");
        }

        var customer = await GetOrCreateCustomerAsync(
            request.CustomerReference, request.Email, request.FirstName, request.LastName, ct);

        if (customer.Id is null)
        {
            throw new MaxioSubscriptionException(
                StatusCodes.Status502BadGateway,
                "The billing service returned a customer without an id.",
                "Maxio customer was created/looked up without an id for reference " + request.CustomerReference);
        }

        var plan = await FindPlanInCatalogAsync(request.PlanHandle, ct);

        // Serialize the pre-check + create per customer so a double-submit cannot race.
        using (await AcquireSubscriptionLockAsync(request.CustomerReference, ct))
        {
            var existing = await FindNonTerminalSubscriptionForPlanAsync(customer.Id.Value, request.PlanHandle, ct);
            if (existing is not null)
            {
                return new SubscribeToPlanResult
                {
                    Subscription = await ToSubscriptionDtoAsync(existing, ct),
                    Created = false
                };
            }

            var subscriptionReference = $"{request.CustomerReference}_{request.PlanHandle}";
            var body = new MaxioCreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerReference = request.CustomerReference,
                    ProductHandle = request.PlanHandle,
                    Reference = subscriptionReference
                }
            };

            SubscriptionResponse response;
            try
            {
                using (WriteOnceHttpMessageHandler.BeginScope())
                {
                    response = await ExecuteWithBudgetAsync(
                        inner => _client.Subscriptions.CreateSubscription(body, inner),
                        ct);
                }
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // Card-free product rejected because the first period's balance is due at
                // signup with no payment method on file. Retry once with a deferred first
                // billing date so the subscription can be created without card capture
                // (the sandbox plans are configured that way). Only a definitive 4xx
                // rejection triggers this, so no duplicate can result.
                if (IsMissingPaymentMethodRejection(ex))
                {
                    _logger.LogInformation(
                        "Maxio requires a payment method for reference {SubscriptionReference} when the first charge is due at signup; retrying with a deferred first billing date.",
                        subscriptionReference);

                    var deferredBody = new MaxioCreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            CustomerReference = request.CustomerReference,
                            ProductHandle = request.PlanHandle,
                            Reference = subscriptionReference,
                            NextBillingAt = ComputeDeferredFirstBillingDate(plan)
                        }
                    };
                    try
                    {
                        using (WriteOnceHttpMessageHandler.BeginScope())
                        {
                            response = await ExecuteWithBudgetAsync(
                                inner => _client.Subscriptions.CreateSubscription(deferredBody, inner),
                                ct);
                        }
                    }
                    catch (SdkException<CreateSubscriptionError> retryEx)
                    {
                        var raced = await FindNonTerminalSubscriptionForPlanAsync(customer.Id.Value, request.PlanHandle, ct);
                        if (raced is not null)
                        {
                            return new SubscribeToPlanResult
                            {
                                Subscription = await ToSubscriptionDtoAsync(raced, ct),
                                Created = false
                            };
                        }

                        LogCreateSubscriptionRejection(retryEx, subscriptionReference);
                        throw new MaxioSubscriptionException(
                            StatusCodes.Status400BadRequest,
                            "The subscription could not be created for the selected plan. The plan may no longer accept new subscribers.");
                    }
                }
                else
                {
                    var raced = await FindNonTerminalSubscriptionForPlanAsync(customer.Id.Value, request.PlanHandle, ct);
                    if (raced is not null)
                    {
                        return new SubscribeToPlanResult
                        {
                            Subscription = await ToSubscriptionDtoAsync(raced, ct),
                            Created = false
                        };
                    }

                    LogCreateSubscriptionRejection(ex, subscriptionReference);
                    throw new MaxioSubscriptionException(
                        StatusCodes.Status400BadRequest,
                        "The subscription could not be created for the selected plan. The plan may no longer accept new subscribers.");
                }
            }
            catch (WriteResendBlockedException)
            {
                // A transport failure may have reached Maxio; establish what actually happened.
                var reconciled = await FindNonTerminalSubscriptionForPlanAsync(customer.Id.Value, request.PlanHandle, ct);
                if (reconciled is not null)
                {
                    return new SubscribeToPlanResult
                    {
                        Subscription = await ToSubscriptionDtoAsync(reconciled, ct),
                        Created = false
                    };
                }

                throw new MaxioSubscriptionException(
                    StatusCodes.Status502BadGateway,
                    "The billing service did not confirm the subscription. Please try again.");
            }

            if (response.Subscription is null)
            {
                throw new MaxioSubscriptionException(
                    StatusCodes.Status502BadGateway,
                    "The billing service returned an empty subscription response.");
            }

            return new SubscribeToPlanResult
            {
                Subscription = await ToSubscriptionDtoAsync(response.Subscription, ct),
                Created = true
            };
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(string customerReference, CancellationToken ct)
    {
        var customer = await FindCustomerOrNullAsync(customerReference, ct);
        if (customer?.Id is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await ExecuteWithBudgetAsync(
                inner => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, inner),
                ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, ex, "list customer subscriptions");
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransport(ex, "list customer subscriptions");
        }
        catch (JsonException ex)
        {
            throw TranslateUnreadableBody(ex);
        }

        var subscriptions = new List<SubscriptionDto>();
        foreach (var response in responses)
        {
            if (response.Subscription is null)
            {
                continue;
            }

            subscriptions.Add(await ToSubscriptionDtoAsync(response.Subscription, ct));
        }

        return subscriptions;
    }

    // ---------------------------------------------------------------------------
    // Customer helpers
    // ---------------------------------------------------------------------------

    private async Task<Customer> GetOrCreateCustomerAsync(
        string reference, string email, string? firstName, string? lastName, CancellationToken ct)
    {
        var existing = await FindCustomerOrNullAsync(reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        var (first, last) = DeriveNames(email, firstName, lastName);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = first,
                LastName = last,
                Email = email,
                Reference = reference
            }
        };

        try
        {
            using (WriteOnceHttpMessageHandler.BeginScope())
            {
                var created = await ExecuteWithBudgetAsync(
                    inner => _client.Customers.CreateCustomer(body, inner),
                    ct);
                if (created.Customer is not null)
                {
                    return created.Customer;
                }
            }
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422 = the reference already exists: a concurrent double-submit lost the race.
                var raced = await FindCustomerOrNullAsync(reference, ct);
                if (raced is not null)
                {
                    return raced;
                }
            }

            LogCreateCustomerRejection(ex, reference);
            throw new MaxioSubscriptionException(
                StatusCodes.Status400BadRequest,
                "The billing customer could not be created for this account.");
        }
        catch (WriteResendBlockedException)
        {
            var reconciled = await FindCustomerOrNullAsync(reference, ct);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw new MaxioSubscriptionException(
                StatusCodes.Status502BadGateway,
                "The billing service did not confirm the customer. Please try again.");
        }

        throw new MaxioSubscriptionException(
            StatusCodes.Status502BadGateway,
            "The billing service returned an empty customer response.");
    }

    private async Task<Customer?> FindCustomerOrNullAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await ExecuteWithBudgetAsync(
                inner => _client.Customers.ReadCustomerByReference(reference, inner),
                ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, ex, "look up billing customer");
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransport(ex, "look up billing customer");
        }
        catch (JsonException ex)
        {
            // A malformed 2xx body is NOT "no customer": never map it onto a miss (a
            // miss here gates customer creation).
            throw TranslateUnreadableBody(ex);
        }
    }

    // ---------------------------------------------------------------------------
    // Plan + subscription helpers
    // ---------------------------------------------------------------------------

    private async Task<Product?> FindPlanInCatalogAsync(string planHandle, CancellationToken ct)
    {
        IReadOnlyList<ProductResponse> responses;
        try
        {
            responses = await ExecuteWithBudgetAsync(
                inner => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + _options.ProductFamilyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: 1,
                    perPage: 20,
                    ct: inner),
                ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            throw TranslateListProductsError(ex);
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransport(ex, "look up subscription plan");
        }
        catch (JsonException ex)
        {
            throw TranslateUnreadableBody(ex);
        }

        var match = responses
            .Select(r => r.Product)
            .FirstOrDefault(p =>
                p is not null &&
                p.ArchivedAt is null &&
                string.Equals(p.Handle, planHandle, StringComparison.Ordinal));

        if (match is null)
        {
            throw new MaxioSubscriptionException(
                StatusCodes.Status404NotFound,
                "The requested subscription plan was not found.");
        }

        return match;
    }

    private async Task<Subscription?> FindNonTerminalSubscriptionForPlanAsync(int customerId, string planHandle, CancellationToken ct)
    {
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await ExecuteWithBudgetAsync(
                inner => _client.Customers.ListCustomerSubscriptions(customerId, inner),
                ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRawError(ex.Error, ex, "look up existing subscriptions");
        }
        catch (HttpRequestException ex)
        {
            throw TranslateTransport(ex, "look up existing subscriptions");
        }
        catch (JsonException ex)
        {
            throw TranslateUnreadableBody(ex);
        }

        return responses
            .Select(r => r.Subscription)
            .FirstOrDefault(s =>
                s is not null &&
                !IsTerminalState(s.State?.Value) &&
                string.Equals(s.Product?.Handle, planHandle, StringComparison.Ordinal));
    }

    private async Task<SubscriptionDto> ToSubscriptionDtoAsync(Subscription subscription, CancellationToken ct)
    {
        var currency = subscription.Currency;
        if (string.IsNullOrWhiteSpace(currency))
        {
            currency = await GetSiteCurrencyOrNullAsync(ct);
        }

        var state = subscription.State?.Value;

        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            Price = ToMajorUnits(subscription.ProductPriceInCents),
            Currency = currency,
            State = state,
            NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            IsActive = state is not null && ActiveStates.Contains(state)
        };
    }

    private async Task<string?> GetSiteCurrencyOrNullAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(SiteCurrencyCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        try
        {
            var response = await ExecuteWithBudgetAsync(inner => _client.Sites.ReadSite(inner), ct);
            var currency = response.Site?.Currency;
            if (string.IsNullOrWhiteSpace(currency))
            {
                _logger.LogWarning("Maxio site reported no default currency.");
                return null;
            }

            _cache.Set(SiteCurrencyCacheKey, currency, CurrencyCacheTtl);
            return currency;
        }
        catch (Exception ex) when (
            ex is SdkException<RawError> or HttpRequestException or JsonException or MaxioSubscriptionException)
        {
            _logger.LogWarning(ex, "Could not read the Maxio site currency; prices will be returned without a currency.");
            return null;
        }
    }

    // ---------------------------------------------------------------------------
    // Concurrency
    // ---------------------------------------------------------------------------

    private async Task<IDisposable> AcquireSubscriptionLockAsync(string customerReference, CancellationToken ct)
    {
        var semaphore = _subscriptionLocks.GetOrAdd(customerReference, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new SubscriptionLockReleaser(semaphore, _subscriptionLocks, customerReference);
    }

    private sealed class SubscriptionLockReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks;
        private readonly string _key;
        private int _disposed;

        public SubscriptionLockReleaser(
            SemaphoreSlim semaphore,
            ConcurrentDictionary<string, SemaphoreSlim> locks,
            string key)
        {
            _semaphore = semaphore;
            _locks = locks;
            _key = key;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _semaphore.Release();
            _locks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(_key, _semaphore));
        }
    }

    // ---------------------------------------------------------------------------
    // Budgeting + error translation (the integration boundary)
    // ---------------------------------------------------------------------------

    private async Task<T> ExecuteWithBudgetAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await operation(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new MaxioSubscriptionException(
                StatusCodes.Status504GatewayTimeout,
                "The billing service did not respond in time. Please try again.");
        }
    }

    private MaxioSubscriptionException TranslateListProductsError(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            _logger.LogError("Maxio product family '{ProductFamilyHandle}' was not found: {Message}",
                _options.ProductFamilyHandle, message);
            return new MaxioSubscriptionException(
                StatusCodes.Status500InternalServerError,
                "The subscription catalog is unavailable.",
                $"The configured Maxio product family '{_options.ProductFamilyHandle}' was not found.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return TranslateRawError(raw, ex, "list subscription plans");
        }

        return new MaxioSubscriptionException(
            StatusCodes.Status502BadGateway,
            "The subscription billing service could not be reached.",
            ex.ToString());
    }

    private MaxioSubscriptionException TranslateRawError(RawError raw, Exception source, string context)
    {
        var providerStatus = (int)raw.StatusCode;
        var body = ReadBodySafely(raw);
        _logger.LogError(source,
            "Maxio {Context} failed with HTTP {StatusCode}: {Body}",
            context, providerStatus, body);

        if (providerStatus >= 500)
        {
            return new MaxioSubscriptionException(
                StatusCodes.Status502BadGateway,
                "The subscription billing service reported an error. Please try again.",
                $"Maxio {context} failed with HTTP {providerStatus}: {body}",
                source);
        }

        if (providerStatus == StatusCodes.Status401Unauthorized || providerStatus == StatusCodes.Status403Forbidden)
        {
            return new MaxioSubscriptionException(
                StatusCodes.Status502BadGateway,
                "The subscription billing service rejected this server's credentials.",
                $"Maxio {context} failed with HTTP {providerStatus}: {body}",
                source);
        }

        return new MaxioSubscriptionException(
            StatusCodes.Status400BadRequest,
            "The subscription billing request was rejected.",
            $"Maxio {context} failed with HTTP {providerStatus}: {body}",
            source);
    }

    private MaxioSubscriptionException TranslateTransport(HttpRequestException ex, string context)
    {
        _logger.LogError(ex, "Maxio {Context} could not be reached.", context);
        return new MaxioSubscriptionException(
            StatusCodes.Status502BadGateway,
            "The subscription billing service could not be reached. Please try again.",
            $"Maxio {context} failed with a transport error: {ex.Message}",
            ex);
    }

    private MaxioSubscriptionException TranslateUnreadableBody(JsonException ex)
    {
        _logger.LogError(ex, "Maxio returned a response that could not be read.");
        return new MaxioSubscriptionException(
            StatusCodes.Status502BadGateway,
            "The subscription billing service returned an unreadable response. Please try again.",
            "A Maxio response could not be deserialized: " + ex.Message,
            ex);
    }

    private void LogCreateCustomerRejection(SdkException<CreateCustomerError> ex, string reference)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out _))
        {
            _logger.LogError(ex, "Maxio rejected customer creation for reference {Reference} (422).", reference);
            return;
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            _logger.LogError(ex, "Maxio rejected customer creation for reference {Reference} (HTTP {StatusCode}): {Body}",
                reference, (int)raw.StatusCode, ReadBodySafely(raw));
            return;
        }

        _logger.LogError(ex, "Maxio rejected customer creation for reference {Reference}.", reference);
    }

    private void LogCreateSubscriptionRejection(SdkException<CreateSubscriptionError> ex, string subscriptionReference)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errors))
        {
            _logger.LogError(ex,
                "Maxio rejected subscription creation for reference {SubscriptionReference}: {Errors}",
                subscriptionReference,
                errors.Errors is null ? string.Empty : string.Join("; ", errors.Errors));
            return;
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            _logger.LogError(ex,
                "Maxio rejected subscription creation for reference {SubscriptionReference} (HTTP {StatusCode}): {Body}",
                subscriptionReference, (int)raw.StatusCode, ReadBodySafely(raw));
            return;
        }

        _logger.LogError(ex, "Maxio rejected subscription creation for reference {SubscriptionReference}.",
            subscriptionReference);
    }

    private static bool IsMissingPaymentMethodRejection(SdkException<CreateSubscriptionError> ex)
    {
        if (!ex.Error.TryGetErrorListResponse1(out var errors))
        {
            return false;
        }

        return errors.Errors is not null &&
               errors.Errors.Any(e => e is not null && e.Contains("payment method", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// First billing date used when a card-free product cannot be created because the
    /// first period's balance is due at signup: defer the first charge by one billing
    /// interval. Maxio then creates the subscription with no payment method captured and
    /// bills at the deferred date (verified against the sandbox).
    /// </summary>
    private static DateTimeOffset ComputeDeferredFirstBillingDate(Product? plan)
    {
        var interval = Math.Max(1, plan?.Interval ?? 1);
        return (plan?.IntervalUnit?.Value) switch
        {
            "day" => DateTimeOffset.UtcNow.AddDays(interval),
            "week" => DateTimeOffset.UtcNow.AddDays(7 * interval),
            "year" => DateTimeOffset.UtcNow.AddYears(interval),
            _ => DateTimeOffset.UtcNow.AddMonths(interval)
        };
    }

    private static string ReadBodySafely(RawError raw)
    {
        try
        {
            return raw.ReadAsString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return "<unreadable error body>";
        }
    }

    private static bool IsTerminalState(string? state) =>
        state is not null && TerminalStates.Contains(state);

    private static decimal? ToMajorUnits(long? cents) =>
        cents.HasValue ? cents.Value / 100m : null;

    private static (string FirstName, string LastName) DeriveNames(string email, string? firstName, string? lastName)
    {
        var (derivedFirst, derivedLast) = DeriveNamesFromEmail(email);
        var effectiveFirst = string.IsNullOrWhiteSpace(firstName) ? derivedFirst : firstName.Trim();
        var effectiveLast = string.IsNullOrWhiteSpace(lastName) ? derivedLast : lastName.Trim();
        return (effectiveFirst, effectiveLast);
    }

    private static (string FirstName, string LastName) DeriveNamesFromEmail(string email)
    {
        var local = email.Split('@')[0];
        var segments = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return ("Subscriber", "Member");
        }

        var first = TitleCase(segments[0]);
        var last = segments.Length > 1
            ? string.Join(" ", segments.Skip(1).Select(TitleCase))
            : first;

        return (first, last);
    }

    private static string TitleCase(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
}
