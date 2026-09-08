using System;
using System.Collections.Concurrent;
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
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Result of an idempotent subscribe operation.
/// </summary>
public sealed class SubscribeResult
{
    public SubscribeResult(SubscriptionDto subscription, bool createdNew)
    {
        Subscription = subscription;
        CreatedNew = createdNew;
    }

    public SubscriptionDto Subscription { get; }

    /// <summary>True when this call created a new subscription; false when an existing live subscription was returned.</summary>
    public bool CreatedNew { get; }
}

/// <summary>
/// Application boundary for Maxio Advanced Billing. Every Maxio interaction happens here, behind this
/// single class, so the SDK never leaks into endpoints or tests. Responsibilities:
/// browse the configured product family, idempotently ensure a Maxio customer for a shopper, create a
/// subscription without capturing a payment method, and read a shopper's subscriptions back. All
/// SDK/protocol failures are translated into caller-safe domain exceptions and the whole-call budget is
/// enforced in one place.
/// </summary>
public sealed class MaxioSubscriptionService
{
    private const int MaxProductsPerPage = 200;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    // Serializes subscribe operations per shopper so a double-click can never create two subscriptions
    // in this process. Idempotency across instances/restarts is provided by re-reading Maxio (the system
    // of record) before creating and reconciling after an ambiguous outcome.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create"
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, MaxioOptions options, ILogger<MaxioSubscriptionService> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    /// <summary>Lists the subscribable plans (products) of the configured product family.</summary>
    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        return await RunBoundedAsync(cancellationToken, ListPlansCoreAsync).ConfigureAwait(false);
    }

    /// <summary>
    /// Subscribes the shopper (identified by <paramref name="email"/>) to the plan with the given handle.
    /// Idempotent: if the shopper already has a live subscription to the plan it is returned unchanged.
    /// </summary>
    public async Task<SubscribeResult> SubscribeAsync(string email, string planHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("An authenticated shopper identity is required.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        return await RunBoundedAsync(cancellationToken, token => SubscribeCoreAsync(email, planHandle.Trim(), token)).ConfigureAwait(false);
    }

    /// <summary>Lists the subscriptions of the Maxio customer that represents the given shopper.</summary>
    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("An authenticated shopper identity is required.", nameof(email));
        }

        return await RunBoundedAsync(cancellationToken, token => ListMySubscriptionsCoreAsync(email, token)).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansCoreAsync(CancellationToken ct)
    {
        var currency = await ReadSiteCurrencyAsync(ct).ConfigureAwait(false);
        var products = await ListFamilyProductsAsync(ct).ConfigureAwait(false);

        var plans = new List<SubscriptionPlanDto>(products.Count);
        foreach (var product in products)
        {
            var plan = ToPlanDto(product, currency);
            if (plan is not null)
            {
                plans.Add(plan);
            }
        }

        return plans;
    }

    private async Task<SubscribeResult> SubscribeCoreAsync(string email, string planHandle, CancellationToken ct)
    {
        var gate = SubscribeLocks.GetOrAdd(email, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var currency = await ReadSiteCurrencyAsync(ct).ConfigureAwait(false);
            var products = await ListFamilyProductsAsync(ct).ConfigureAwait(false);
            var plan = products.FirstOrDefault(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            var planDto = ToPlanDto(plan, currency) ?? new SubscriptionPlanDto { Handle = planHandle };
            var customer = await EnsureCustomerAsync(email, ct).ConfigureAwait(false);
            var customerId = customer.Id
                ?? throw new MaxioUnavailableException("The billing provider did not return a customer id.");

            var existing = await FindLiveSubscriptionAsync(customerId, planHandle, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return new SubscribeResult(ToSubscriptionDto(existing, planDto, currency) ?? new SubscriptionDto(), createdNew: false);
            }

            var reference = $"{planHandle}-{Guid.NewGuid():N}";
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerReference = email,
                    Reference = reference,
                    PaymentCollectionMethod = CollectionMethod.Automatic,
                    // No payment method is captured in this capability, so the plan's first charge must
                    // not be attempted at signup. A future next_billing_at defers the first capture to the
                    // first scheduled renewal (documented Maxio behaviour for card-less signup).
                    NextBillingAt = FirstBillingAfter(plan)
                }
            };

            SubscriptionResponse? attempt = null;
            Exception? createError = null;
            try
            {
                using (MaxioSingleSendHandler.EnterScope())
                {
                    attempt = await _client.Subscriptions.CreateSubscription(body: body, ct: ct).ConfigureAwait(false);
                }
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // A typed provider rejection: the create will never succeed as sent, so do not reconcile.
                ThrowSubscriptionCreateRejected(ex);
            }
            catch (Exception ex) when (ex is MaxioResendRefusedException or HttpRequestException or JsonException or OperationCanceledException)
            {
                // Transport failure, refused resend, unreadable body, or timeout: the create may or may not
                // have reached the provider. Settle the outcome by re-reading provider state.
                createError = ex;
            }

            if (attempt?.Subscription is { } created)
            {
                _logger.LogInformation("Created Maxio subscription {SubscriptionId} for customer {CustomerId} on plan {PlanHandle}.",
                    created.Id, customerId, planHandle);
                return new SubscribeResult(ToSubscriptionDto(created, planDto, currency) ?? new SubscriptionDto(), createdNew: true);
            }

            var reconciled = await FindLiveSubscriptionAsync(customerId, planHandle, ct).ConfigureAwait(false);
            if (reconciled is not null)
            {
                if (createError is not null)
                {
                    _logger.LogWarning(createError,
                        "Subscription create reported an error but reconciliation found subscription {SubscriptionId}; treating it as created.",
                        reconciled.Id);
                }

                return new SubscribeResult(ToSubscriptionDto(reconciled, planDto, currency) ?? new SubscriptionDto(), createdNew: true);
            }

            if (createError is not null)
            {
                throw new MaxioUnavailableException(
                    "The subscription could not be created and the billing provider could not confirm whether it succeeded. Please try again.", createError);
            }

            throw new MaxioUnavailableException(
                "The billing provider accepted the subscription request but did not confirm the new subscription. Please try again.");
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsCoreAsync(string email, CancellationToken ct)
    {
        var customer = await ReadCustomerByReferenceAsync(email, ct).ConfigureAwait(false);
        if (customer is null || customer.Id is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, ct).ConfigureAwait(false);
        if (subscriptions.Count == 0)
        {
            return Array.Empty<SubscriptionDto>();
        }

        string? currency = null;
        if (subscriptions.Any(s => string.IsNullOrEmpty(s.Currency)))
        {
            currency = await ReadSiteCurrencyAsync(ct).ConfigureAwait(false);
        }

        return subscriptions
            .Select(s => ToSubscriptionDto(s, plan: null, currency))
            .Where(dto => dto is not null)
            .Select(dto => dto!)
            .ToList();
    }

    private async Task<string?> ReadSiteCurrencyAsync(CancellationToken ct)
    {
        try
        {
            var response = await _client.Sites.ReadSite(ct: ct).ConfigureAwait(false);
            return response.Site?.Currency;
        }
        catch (SdkException<RawError> ex)
        {
            throw ReadFailure(ex, "read the billing site");
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioUnavailableException("The billing provider could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioUnavailableException("The billing provider returned a response that could not be processed.", ex);
        }
    }

    private async Task<List<Product>> ListFamilyProductsAsync(CancellationToken ct)
    {
        var familyHandle = RequireFamilyHandle();
        var products = new List<Product>();

        int page = 1;
        while (true)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: "handle:" + familyHandle,
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: MaxProductsPerPage,
                    ct: ct).ConfigureAwait(false);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw ListProductsFailure(ex, familyHandle);
            }
            catch (HttpRequestException ex)
            {
                throw new MaxioUnavailableException("The billing provider could not be reached.", ex);
            }
            catch (JsonException ex)
            {
                throw new MaxioUnavailableException("The billing provider returned a response that could not be processed.", ex);
            }

            if (pageItems is null || pageItems.Count == 0)
            {
                break;
            }

            foreach (var item in pageItems)
            {
                if (item.Product is not null)
                {
                    products.Add(item.Product);
                }
            }

            if (pageItems.Count < MaxProductsPerPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    /// <summary>
    /// Returns the Maxio customer for <paramref name="email"/>, creating one if it does not exist yet.
    /// The customer reference is the shopper's email (the stable identifier carried by the caller's JWT),
    /// which Maxio enforces as unique — that uniqueness is the backstop that makes concurrent creation
    /// safe: a duplicate create is rejected and the winner is re-read.
    /// </summary>
    private async Task<Customer> EnsureCustomerAsync(string email, CancellationToken ct)
    {
        var existing = await ReadCustomerByReferenceAsync(email, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        return await CreateCustomerAsync(email, ct).ConfigureAwait(false);
    }

    private async Task<Customer?> ReadCustomerByReferenceAsync(string email, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference: email, ct: ct).ConfigureAwait(false);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw ReadFailure(ex, "look up the billing customer");
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioUnavailableException("The billing provider could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioUnavailableException("The billing provider returned a response that could not be processed.", ex);
        }
    }

    private async Task<Customer> CreateCustomerAsync(string email, CancellationToken ct)
    {
        var (firstName, lastName) = DeriveCustomerName(email);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = email
            }
        };

        try
        {
            var response = await _client.Customers.CreateCustomer(body: body, ct: ct).ConfigureAwait(false);
            var created = response.Customer;
            if (created is null)
            {
                throw new MaxioUnavailableException("The billing provider accepted the customer but returned no customer details.");
            }

            _logger.LogInformation("Created Maxio customer {CustomerId} for {Email}.", created.Id, email);
            return created;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422. For a create with a reference that already exists this is the documented duplicate
                // signal (a concurrent create won the race): re-read the winner. If no customer now exists
                // the 422 was a genuine validation rejection.
                var winner = await ReadCustomerByReferenceAsync(email, ct).ConfigureAwait(false);
                if (winner is not null)
                {
                    return winner;
                }

                throw new MaxioRequestRejectedException("The billing customer could not be created.");
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                if (IsCredentialsRejection(raw.StatusCode))
                {
                    throw new MaxioConfigurationException("The billing provider rejected the configured credentials.");
                }

                throw new MaxioUnavailableException("The billing provider rejected the customer creation request.", ex);
            }

            throw new MaxioUnavailableException("The billing provider rejected the customer creation request.", ex);
        }
        catch (MaxioResendRefusedException)
        {
            // The resend of the create was refused; the first attempt may have succeeded. Re-read to settle.
            var winner = await ReadCustomerByReferenceAsync(email, ct).ConfigureAwait(false);
            if (winner is not null)
            {
                return winner;
            }

            throw new MaxioUnavailableException("The billing provider could not be reached while creating the billing customer.");
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioUnavailableException("The billing provider could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioUnavailableException("The billing provider returned a response that could not be processed.", ex);
        }
    }

    private async Task<List<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct).ConfigureAwait(false);
            var subscriptions = new List<Subscription>();
            if (response is not null)
            {
                foreach (var item in response)
                {
                    if (item?.Subscription is { } subscription)
                    {
                        subscriptions.Add(subscription);
                    }
                }
            }

            return subscriptions;
        }
        catch (SdkException<RawError> ex)
        {
            throw ReadFailure(ex, "list the shopper's subscriptions");
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioUnavailableException("The billing provider could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioUnavailableException("The billing provider returned a response that could not be processed.", ex);
        }
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct).ConfigureAwait(false);
        return subscriptions.FirstOrDefault(s => MatchesPlan(s, planHandle) && IsLive(s));
    }

    private static bool MatchesPlan(Subscription subscription, string planHandle)
    {
        if (subscription.Product?.Handle is { } productHandle &&
            string.Equals(productHandle, planHandle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (subscription.Reference is { } reference &&
            string.Equals(reference, planHandle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsLive(Subscription subscription)
    {
        return subscription.State is null || !TerminalStates.Contains(subscription.State.Value);
    }

    private string RequireFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio product family is not configured. Set Maxio:ProductFamilyHandle (or MAXIO_DEFAULT_PRODUCT_FAMILY).");
        }

        return _options.ProductFamilyHandle;
    }

    private async Task<T> RunBoundedAsync<T>(CancellationToken callerToken, Func<CancellationToken, Task<T>> action)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(CallTimeout);
        try
        {
            return await action(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new MaxioUnavailableException("The billing provider did not respond in time. Please try again.", ex);
        }
    }

    private static SubscriptionPlanDto? ToPlanDto(Product product, string? currency)
    {
        if (string.IsNullOrWhiteSpace(product.Handle))
        {
            return null;
        }

        return new SubscriptionPlanDto
        {
            Handle = product.Handle,
            Name = product.Name,
            Price = ToPrice(product.PriceInCents),
            Currency = currency,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value
        };
    }

    private static SubscriptionDto? ToSubscriptionDto(Subscription subscription, SubscriptionPlanDto? plan, string? siteCurrency)
    {
        var product = subscription.Product;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            PlanHandle = product?.Handle ?? plan?.Handle,
            PlanName = product?.Name ?? plan?.Name,
            Price = ToPrice(subscription.ProductPriceInCents) ?? ToPrice(product?.PriceInCents) ?? plan?.Price,
            Currency = subscription.Currency ?? siteCurrency ?? plan?.Currency,
            Interval = product?.Interval ?? plan?.Interval,
            IntervalUnit = product?.IntervalUnit?.Value ?? plan?.IntervalUnit,
            State = subscription.State?.Value,
            NextBillingDate = subscription.CurrentPeriodEndsAt
        };
    }

    private static decimal? ToPrice(long? cents)
    {
        return cents is null ? null : Math.Round(cents.Value / 100m, 2);
    }

    private static DateTimeOffset FirstBillingAfter(Product product)
    {
        var now = DateTimeOffset.UtcNow;
        var count = Math.Max(1, product.Interval ?? 1);
        return (product.IntervalUnit?.Value) switch
        {
            "day" => now.AddDays(count),
            "week" => now.AddDays(7 * count),
            "month" => now.AddMonths(count),
            "year" => now.AddMonths(12 * count),
            _ => now.AddMonths(count)
        };
    }

    private static void ThrowSubscriptionCreateRejected(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var errorList) && errorList?.Errors is { Count: > 0 })
        {
            var detail = string.Join(" ", errorList.Errors);
            throw new MaxioRequestRejectedException(
                "The billing provider rejected the subscription" + (string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}"));
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            if (IsCredentialsRejection(raw.StatusCode))
            {
                throw new MaxioConfigurationException("The billing provider rejected the configured credentials.");
            }

            throw new MaxioUnavailableException("The billing provider rejected the subscription request.", ex);
        }

        throw new MaxioUnavailableException("The billing provider rejected the subscription request.", ex);
    }

    private static Exception ListProductsFailure(SdkException<ListProductsForProductFamilyError> ex, string familyHandle)
    {
        if (ex.Error.TryGetString(out _))
        {
            return new MaxioConfigurationException($"The configured Maxio product family '{familyHandle}' was not found.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            if (IsCredentialsRejection(raw.StatusCode))
            {
                return new MaxioConfigurationException("The billing provider rejected the configured credentials.");
            }

            return new MaxioUnavailableException("The billing provider returned an error while listing subscription plans.", ex);
        }

        return new MaxioUnavailableException("The billing provider returned an error while listing subscription plans.", ex);
    }

    private static Exception ReadFailure(SdkException<RawError> ex, string what)
    {
        if (IsCredentialsRejection(ex.Error.StatusCode))
        {
            return new MaxioConfigurationException("The billing provider rejected the configured credentials.");
        }

        if (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return new MaxioConfigurationException($"The billing provider could not find the resource while trying to {what}.");
        }

        return new MaxioUnavailableException($"The billing provider returned an error while trying to {what}.", ex);
    }

    private static bool IsCredentialsRejection(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
    }

    private static (string FirstName, string LastName) DeriveCustomerName(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        var domain = at > 0 ? email[(at + 1)..] : string.Empty;
        var host = domain.Split('.')[0];
        var tokens = local.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length >= 2)
        {
            return (Capitalize(tokens[0]), Capitalize(tokens[^1]));
        }

        if (tokens.Length == 1)
        {
            return (Capitalize(tokens[0]), string.IsNullOrEmpty(host) ? "Customer" : Capitalize(host));
        }

        return ("Customer", string.IsNullOrEmpty(host) ? "Customer" : Capitalize(host));
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length == 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }
}
