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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by Maxio Advanced Billing.
/// Idempotency: the billing customer's reference is the eShopOnWeb user id (uniqueness is
/// enforced provider-side); each subscription carries a deterministic reference
/// "{userId}:{productHandle}" that is checked before any create.
/// </summary>
public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        IAppLogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            var family = await ResolveProductFamilyAsync(ct);

            var products = await Guarded(
                () => _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.Id!.Value.ToString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: 1,
                    perPage: 200,
                    ct: ct),
                "listing subscription plans");

            return (IReadOnlyList<SubscriptionPlanDto>)products
                .Select(p => p.Product)
                .Where(p => p.ArchivedAt is null)
                .Select(p => new SubscriptionPlanDto
                {
                    Handle = p.Handle ?? string.Empty,
                    Name = p.Name ?? string.Empty,
                    Price = (p.PriceInCents ?? 0) / 100m,
                    Interval = p.Interval ?? 1,
                    IntervalUnit = p.IntervalUnit?.Value ?? string.Empty
                })
                .ToList();
        }, cancellationToken);
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userId, string email, string firstName, string lastName,
        string productHandle, CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            var customer = await EnsureCustomerAsync(userId, email, firstName, lastName, ct);
            var reference = $"{userId}:{productHandle}";

            var existing = await FindSubscriptionByReferenceAsync(reference, ct);
            if (existing is not null)
            {
                return ToDto(existing);
            }

            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = productHandle,
                    CustomerId = customer.Id,
                    Reference = reference
                }
            };

            try
            {
                var response = await _client.Subscriptions.CreateSubscription(body: body, ct: ct);
                if (response.Subscription is null)
                {
                    throw new BillingException("The billing provider returned an empty subscription.",
                        HttpStatusCode.BadGateway);
                }
                return ToDto(response.Subscription);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                // A 422 here can mean a racing request already created the subscription with the
                // same deterministic reference — re-read before reporting failure.
                var raced = await FindSubscriptionByReferenceAsync(reference, ct);
                if (raced is not null)
                {
                    return ToDto(raced);
                }

                if (ex.Error.TryGetErrorListResponse1(out var errorList))
                {
                    _logger.LogWarning($"Maxio rejected subscription create: {string.Join("; ", errorList.Errors)}");
                    throw new BillingException(
                        $"The subscription could not be created: {string.Join("; ", errorList.Errors)}",
                        HttpStatusCode.UnprocessableEntity, ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRaw(raw, "creating the subscription", ex);
                }
                throw new BillingException("The billing provider rejected the subscription.",
                    HttpStatusCode.UnprocessableEntity, ex);
            }
            catch (JsonException ex)
            {
                throw UnprocessableResponse(ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw ProviderUnreachable(ex);
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await Bounded<IReadOnlyList<SubscriptionDto>>(async ct =>
        {
            Customer? customer;
            try
            {
                customer = (await _client.Customers.ReadCustomerByReference(userId, ct: ct)).Customer;
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return Array.Empty<SubscriptionDto>();
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRaw(ex.Error, "looking up the billing customer", ex);
            }
            catch (JsonException ex)
            {
                throw UnprocessableResponse(ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw ProviderUnreachable(ex);
            }

            if (customer?.Id is null)
            {
                return Array.Empty<SubscriptionDto>();
            }

            var subscriptions = await Guarded(
                () => _client.Customers.ListCustomerSubscriptions(customer.Id.Value, ct: ct),
                "listing subscriptions");

            return subscriptions
                .Select(s => s.Subscription)
                .Where(s => s is not null)
                .Select(s => ToDto(s!))
                .ToList();
        }, cancellationToken);
    }

    private async Task<ProductFamily> ResolveProductFamilyAsync(CancellationToken ct)
    {
        var families = await Guarded(
            () => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct),
            "listing product families");

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f?.Handle == _settings.ProductFamilyHandle && f.Id is not null);

        if (family is null)
        {
            throw new BillingException(
                "The configured billing product family was not found.",
                HttpStatusCode.BadGateway);
        }
        return family;
    }

    private async Task<Customer> EnsureCustomerAsync(string userId, string email, string firstName, string lastName,
        CancellationToken ct)
    {
        try
        {
            return (await _client.Customers.ReadCustomerByReference(userId, ct: ct)).Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // expected on first subscribe — fall through to create
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, "looking up the billing customer", ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw ProviderUnreachable(ex);
        }

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
            return (await _client.Customers.CreateCustomer(body: body, ct: ct)).Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // Any 422 can mean a racing request already created the customer with this reference
            // (the generated error model does not carry field-level detail) — re-read by reference.
            try
            {
                return (await _client.Customers.ReadCustomerByReference(userId, ct: ct)).Customer;
            }
            catch (Exception readEx) when (readEx is SdkException<RawError> or JsonException
                or HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning($"Maxio rejected customer create and re-read failed: {readEx.Message}");
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRaw(raw, "creating the billing customer", ex);
                }
                throw new BillingException("The billing provider rejected the customer.",
                    HttpStatusCode.UnprocessableEntity, ex);
            }
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw ProviderUnreachable(ex);
        }
    }

    private async Task<Subscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            return (await _client.Subscriptions.FindSubscription(reference: reference, ct: ct)).Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, "checking for an existing subscription", ex);
            }
            throw new BillingException("The billing provider rejected the subscription lookup.",
                HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw ProviderUnreachable(ex);
        }
    }

    // Shared guard for the Case-B (SdkException<RawError>) read operations.
    private async Task<T> Guarded<T>(Func<Task<T>> call, string operation)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, operation, ex);
        }
        catch (JsonException ex)
        {
            throw UnprocessableResponse(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw ProviderUnreachable(ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private BillingException TranslateRaw(RawError raw, string operation, Exception inner)
    {
        var status = raw.StatusCode;
        _logger.LogWarning($"Maxio error while {operation}: HTTP {(int)status} {raw.ReadAsString()}");
        // Provider 4xx is client-actionable and is carried through; anything else is a bad gateway.
        var surface = (int)status is >= 400 and < 500 ? status : HttpStatusCode.BadGateway;
        return new BillingException($"The billing provider could not complete the request ({(int)status}).",
            surface, inner);
    }

    private static BillingException UnprocessableResponse(JsonException inner) =>
        new("The billing provider returned a response that could not be processed.",
            HttpStatusCode.BadGateway, inner);

    private static BillingException ProviderUnreachable(Exception inner) =>
        new("The billing provider is unreachable.", HttpStatusCode.ServiceUnavailable, inner);

    private static SubscriptionDto ToDto(Subscription s) => new()
    {
        SubscriptionId = s.Id ?? 0,
        Reference = s.Reference ?? string.Empty,
        State = s.State?.Value ?? string.Empty,
        PlanHandle = s.Product?.Handle ?? string.Empty,
        PlanName = s.Product?.Name ?? string.Empty,
        Price = (s.ProductPriceInCents ?? 0) / 100m,
        NextBillingDate = s.NextAssessmentAt ?? s.CurrentPeriodEndsAt
    };
}
