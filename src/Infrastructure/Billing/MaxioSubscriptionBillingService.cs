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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing .NET SDK.
/// Customer identity is keyed on the eShopOnWeb user name stored as the Maxio customer
/// <c>reference</c>; subscriptions carry a deterministic "{user}:{productHandle}" reference
/// so retries and double-clicks never create duplicates.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    public const string HttpClientName = "Maxio";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserGates = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;
    private int? _productFamilyId;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
        => Guarded<IReadOnlyList<SubscriptionPlanDto>>(async () =>
        {
            int familyId = await ResolveProductFamilyIdAsync(cancellationToken);

            try
            {
                var products = await Bounded(ct => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: 1,
                    perPage: 100,
                    ct: ct), cancellationToken);

                return products
                    .Select(p => p.Product)
                    .Where(p => p is not null && p.ArchivedAt is null)
                    .Select(p => new SubscriptionPlanDto
                    {
                        Handle = p.Handle ?? string.Empty,
                        Name = p.Name ?? string.Empty,
                        Description = p.Description,
                        PriceInCents = p.PriceInCents ?? 0,
                        Interval = p.Interval ?? 1,
                        IntervalUnit = p.IntervalUnit?.Value ?? string.Empty
                    })
                    .ToList();
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw new BillingException((int)HttpStatusCode.InternalServerError,
                        "The billing catalog is not configured correctly.");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw Translate(raw);
                }
                throw ProviderError();
            }
        });

    public Task<CustomerSubscriptionDto> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken = default)
        => Guarded(async () =>
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new BillingException((int)HttpStatusCode.Unauthorized, "The caller identity is missing.");
            }
            if (string.IsNullOrWhiteSpace(productHandle))
            {
                throw new BillingException((int)HttpStatusCode.BadRequest, "A product handle is required.");
            }

            // Serialize the check-then-create sequence per user so concurrent requests
            // (e.g. a double-click) cannot race past the idempotency pre-check.
            var gate = UserGates.GetOrAdd(userName, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                string reference = $"{userName}:{productHandle}";

                var existing = await FindSubscriptionByReferenceAsync(reference, cancellationToken);
                if (existing is not null)
                {
                    return Map(existing);
                }

                // Validate the plan handle before creating anything.
                await GetPlanByHandleAsync(productHandle, cancellationToken);

                var customer = await EnsureCustomerAsync(userName, cancellationToken);

                try
                {
                    var created = await Bounded(ct => _client.Subscriptions.CreateSubscription(
                        body: new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = productHandle,
                                CustomerId = customer.Id,
                                Reference = reference,
                                // Card-less signup: bill by remittance/invoice instead of an
                                // automatic card charge (no payment profile is ever collected).
                                PaymentCollectionMethod = CollectionMethod.Remittance
                            }
                        },
                        ct: ct), cancellationToken);

                    if (created.Subscription is null)
                    {
                        throw ProviderUnprocessable();
                    }
                    return Map(created.Subscription);
                }
                catch (SdkException<CreateSubscriptionError> ex)
                {
                    if (ex.Error.TryGetErrorListResponse1(out var errors))
                    {
                        throw new BillingException((int)HttpStatusCode.UnprocessableEntity,
                            $"The billing provider rejected the subscription: {string.Join("; ", errors.Errors)}");
                    }
                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw Translate(raw);
                    }
                    throw ProviderError();
                }
            }
            finally
            {
                gate.Release();
            }
        });

    public Task<IReadOnlyList<CustomerSubscriptionDto>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default)
        => Guarded<IReadOnlyList<CustomerSubscriptionDto>>(async () =>
        {
            Customer customer;
            try
            {
                var response = await Bounded(ct => _client.Customers.ReadCustomerByReference(
                    reference: userName,
                    ct: ct), cancellationToken);
                if (response.Customer is not { Id: not null } found)
                {
                    throw ProviderUnprocessable();
                }
                customer = found;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                // No billing customer yet => the user has never subscribed.
                return Array.Empty<CustomerSubscriptionDto>();
            }

            var subscriptions = await Bounded(ct => _client.Customers.ListCustomerSubscriptions(
                customerId: customer.Id!.Value,
                ct: ct), cancellationToken);

            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => Map(s!))
                .ToList();
        });

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        if (_productFamilyId is int cached)
        {
            return cached;
        }
        if (string.IsNullOrWhiteSpace(_settings.ProductFamilyHandle))
        {
            throw new BillingException((int)HttpStatusCode.InternalServerError,
                "The Maxio product family is not configured.");
        }

        var families = await Bounded(c => _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: c), ct);

        var match = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => string.Equals(f?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (match?.Id is not int id)
        {
            throw new BillingException((int)HttpStatusCode.InternalServerError,
                "The configured Maxio product family was not found.");
        }

        _productFamilyId = id;
        return id;
    }

    private async Task<Product> GetPlanByHandleAsync(string productHandle, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(c => _client.Products.ReadProductByHandle(
                apiHandle: productHandle,
                ct: c), ct);
            if (response.Product is null)
            {
                throw ProviderUnprocessable();
            }
            return response.Product;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BillingException((int)HttpStatusCode.NotFound,
                $"No subscription plan with handle '{productHandle}' exists.");
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(c => _client.Subscriptions.FindSubscription(
                reference: reference,
                ct: c), ct);
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
                throw Translate(raw);
            }
            throw ProviderError();
        }
    }

    private async Task<Customer> EnsureCustomerAsync(string userName, CancellationToken ct)
    {
        try
        {
            var existing = await Bounded(c => _client.Customers.ReadCustomerByReference(
                reference: userName,
                ct: c), ct);
            if (existing.Customer is { Id: not null } found)
            {
                return found;
            }
            throw ProviderUnprocessable();
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // No customer for this reference yet — create one below.
        }

        try
        {
            var created = await Bounded(c => _client.Customers.CreateCustomer(
                body: new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = DeriveNamePart(userName, first: true),
                        LastName = DeriveNamePart(userName, first: false),
                        Email = userName,
                        Reference = userName
                    }
                },
                ct: c), ct);

            if (created.Customer is { Id: not null } createdCustomer)
            {
                return createdCustomer;
            }
            throw ProviderUnprocessable();
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                // 422 on create — the typed body cannot carry customer-field messages, so never
                // branch on it. Treat it as a possible reference race and re-lookup; if the
                // customer now exists, another request created it first and we use it.
                var raced = await Bounded(c => _client.Customers.ReadCustomerByReference(
                    reference: userName,
                    ct: c), ct);
                if (raced.Customer is { Id: not null } racedCustomer)
                {
                    return racedCustomer;
                }
                throw new BillingException((int)HttpStatusCode.UnprocessableEntity,
                    "The billing provider rejected the customer details.");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw);
            }
            throw ProviderError();
        }
    }

    private static CustomerSubscriptionDto Map(Subscription s) => new()
    {
        Id = s.Id ?? 0,
        Reference = s.Reference,
        State = s.State?.Value ?? string.Empty,
        ProductHandle = s.Product?.Handle ?? string.Empty,
        ProductName = s.Product?.Name ?? string.Empty,
        PriceInCents = s.ProductPriceInCents,
        Currency = s.Currency,
        // The SDK model has no next_billing_at; next assessment is the next billing event,
        // with the period end as fallback.
        NextBillingDate = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt
    };

    private static string DeriveNamePart(string email, bool first)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return first ? "Customer" : "Account";
        }
        var part = first ? parts[0] : parts[^1];
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part.ToLowerInvariant());
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        // The only whole-call bound: per-attempt timeouts (Retry/HttpClient) do not cap retries.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task<T> Guarded<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (BillingException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error);
        }
        catch (JsonException ex)
        {
            // A 2xx whose body drifted from the model, or an error body that did not match its
            // generated error shape — either way the outcome is unknown, so surface a 5xx.
            _logger.LogWarning("Maxio response could not be processed: {Message}", ex.Message);
            throw ProviderUnprocessable();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Maxio call failed at transport level: {Message}", ex.Message);
            throw new BillingException((int)HttpStatusCode.ServiceUnavailable,
                "The billing provider is unreachable or timed out.");
        }
    }

    private BillingException Translate(RawError raw)
    {
        int status = (int)raw.StatusCode;
        _logger.LogWarning("Maxio API error {StatusCode}: {Body}", status, raw.ReadAsString());
        return status is >= 400 and < 500
            ? new BillingException(status, "The billing provider rejected the request.")
            : ProviderError();
    }

    private static BillingException ProviderError()
        => new((int)HttpStatusCode.BadGateway, "The billing provider returned an error.");

    private static BillingException ProviderUnprocessable()
        => new((int)HttpStatusCode.BadGateway, "The billing provider returned a response that could not be processed.");
}
