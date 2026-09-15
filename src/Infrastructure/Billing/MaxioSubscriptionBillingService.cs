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
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionState.Canceled.Value,
        SubscriptionState.Expired.Value,
        SubscriptionState.FailedToCreate.Value,
        SubscriptionState.TrialEnded.Value
    };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly IMaxioCallBudget _budget;
    private readonly IAppLogger<MaxioSubscriptionBillingService> _logger;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        IMaxioCallBudget budget,
        IAppLogger<MaxioSubscriptionBillingService> logger,
        IOptions<MaxioOptions> options)
    {
        _client = client;
        _budget = budget;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var familyHandle = _options.ProductFamilyHandle.Trim();

        try
        {
            var products = await ListAllProductsForFamilyAsync(familyHandle, cancellationToken);
            return products
                .Select(p => p.Product)
                .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Handle))
                .Select(MapPlan)
                .ToList();
        }
        catch (BillingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Unable to list subscription plans.");
        }
    }

    public async Task<ShopSubscription> SubscribeAsync(string userId, string? productHandle, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new BillingException(400, "A signed-in user is required to subscribe.");
        }

        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new BillingException(400, "A product handle is required.");
        }

        var handle = productHandle.Trim();
        var reference = SubscriptionReference(userId, handle);

        try
        {
            await EnsureProductExistsAsync(handle, cancellationToken);
            var customer = await EnsureCustomerAsync(userId, cancellationToken);

            var existing = await FindLiveSubscriptionAsync(customer.Id!.Value, userId, handle, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            MaxioWriteGate.BeginWrite();
            MaxioLastStatus.Clear();
            SubscriptionResponse created;
            try
            {
                created = await _budget.RunAsync(
                    ct => _client.Subscriptions.CreateSubscription(
                        body: new CreateSubscriptionRequest
                        {
                            Subscription = new CreateSubscription
                            {
                                ProductHandle = handle,
                                CustomerId = customer.Id,
                                CustomerReference = userId,
                                Reference = reference,
                                PaymentCollectionMethod = CollectionMethod.Invoice
                            }
                        },
                        ct: ct),
                    cancellationToken);
            }
            catch (MaxioDuplicateWriteException)
            {
                return await RequireExistingAfterWriteAsync(customer.Id.Value, userId, handle, cancellationToken);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                var recovered = await TryFindLiveSubscriptionAsync(customer.Id.Value, userId, handle, cancellationToken);
                if (recovered is not null)
                {
                    return recovered;
                }

                throw TranslateCreateSubscription(ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or MaxioDuplicateWriteException)
            {
                var recovered = await TryFindLiveSubscriptionAsync(customer.Id.Value, userId, handle, cancellationToken);
                if (recovered is not null)
                {
                    return recovered;
                }

                throw Translate(ex, "Unable to create the subscription.");
            }

            var subscription = created.Subscription;
            if (subscription is null)
            {
                throw new BillingException(502, "The billing provider returned a response that could not be processed.");
            }

            return MapSubscription(subscription);
        }
        catch (BillingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Unable to create the subscription.");
        }
    }

    public async Task<IReadOnlyList<ShopSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new BillingException(400, "A signed-in user is required to list subscriptions.");
        }

        try
        {
            var customer = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
            if (customer is null)
            {
                return Array.Empty<ShopSubscription>();
            }

            var rows = await ReadAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customerId: customer.Id!.Value, ct: ct),
                cancellationToken);

            return rows
                .Select(r => r.Subscription)
                .Where(s => s is not null)
                .Select(s => MapSubscription(s!))
                .ToList();
        }
        catch (BillingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Unable to list subscriptions.");
        }
    }

    private async Task<IReadOnlyList<ProductResponse>> ListAllProductsForFamilyAsync(string familyHandle, CancellationToken cancellationToken)
    {
        var all = new List<ProductResponse>();
        var page = 1;
        const int perPage = 20;
        while (true)
        {
            IReadOnlyList<ProductResponse> batch;
            try
            {
                batch = await ReadAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
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
                        perPage: perPage,
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                throw TranslateListProducts(ex);
            }

            all.AddRange(batch);
            if (batch.Count < perPage)
            {
                break;
            }

            page++;
        }

        return all;
    }

    private async Task EnsureProductExistsAsync(string handle, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ReadAsync(
                ct => _client.Products.ReadProductByHandle(apiHandle: handle, ct: ct),
                cancellationToken);
            if (response.Product is null)
            {
                throw new BillingException(400, $"Unknown subscription plan '{handle}'.");
            }
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            throw new BillingException(400, $"Unknown subscription plan '{handle}'.");
        }
    }

    private async Task<Customer> EnsureCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName, email) = SplitIdentity(userId);
        MaxioWriteGate.BeginWrite();
        MaxioLastStatus.Clear();
        try
        {
            var created = await _budget.RunAsync(
                ct => _client.Customers.CreateCustomer(
                    body: new CreateCustomerRequest
                    {
                        Customer = new CreateCustomer
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email,
                            Reference = userId
                        }
                    },
                    ct: ct),
                cancellationToken);
            return created.Customer;
        }
        catch (SdkException<CreateCustomerError>)
        {
            var raced = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw new BillingException(409, "Unable to create a billing customer for this account.");
        }
        catch (MaxioDuplicateWriteException)
        {
            var raced = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw new BillingException(502, "The billing provider request completed with an unknown outcome.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            var raced = await TryReadCustomerByReferenceAsync(userId, cancellationToken);
            if (raced is not null)
            {
                return raced;
            }

            throw Translate(ex, "Unable to create a billing customer for this account.");
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ReadAsync(
                ct => _client.Customers.ReadCustomerByReference(reference: userId, ct: ct),
                cancellationToken);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<ShopSubscription?> FindLiveSubscriptionAsync(
        int customerId,
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var byReference = await TryFindBySubscriptionReferenceAsync(userId, productHandle, cancellationToken);
        if (byReference is not null && IsLive(byReference))
        {
            return MapSubscription(byReference);
        }

        var listed = await ReadAsync(
            ct => _client.Customers.ListCustomerSubscriptions(customerId: customerId, ct: ct),
            cancellationToken);

        var match = listed
            .Select(r => r.Subscription)
            .FirstOrDefault(s => s is not null && IsLive(s) && HandlesEqual(s.Product?.Handle, productHandle));

        return match is null ? null : MapSubscription(match);
    }

    private async Task<ShopSubscription?> TryFindLiveSubscriptionAsync(
        int customerId,
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FindLiveSubscriptionAsync(customerId, userId, productHandle, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not reconcile subscription after a write: {Message}", ex.Message);
            return null;
        }
    }

    private async Task<ShopSubscription> RequireExistingAfterWriteAsync(
        int customerId,
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        var recovered = await TryFindLiveSubscriptionAsync(customerId, userId, productHandle, cancellationToken);
        if (recovered is not null)
        {
            return recovered;
        }

        throw new BillingException(502, "The billing provider request completed with an unknown outcome.");
    }

    private async Task<Subscription?> TryFindBySubscriptionReferenceAsync(
        string userId,
        string productHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ReadAsync(
                ct => _client.Subscriptions.FindSubscription(
                    reference: SubscriptionReference(userId, productHandle),
                    ct: ct),
                cancellationToken);
            return response.Subscription;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw) && (int)raw.StatusCode == 404)
            {
                return null;
            }

            throw TranslateFindSubscription(ex);
        }
    }

    private async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        MaxioLastStatus.Clear();
        return await _budget.RunAsync(call, cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
            (string.IsNullOrWhiteSpace(_options.Subdomain) && string.IsNullOrWhiteSpace(_options.BaseUrl)) ||
            string.IsNullOrWhiteSpace(_options.ProductFamilyHandle))
        {
            throw new BillingException(500, "Maxio billing is not configured.");
        }
    }

    private static string SubscriptionReference(string userId, string productHandle)
    {
        var raw = $"{userId}-{productHandle}";
        var chars = raw.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray();
        return new string(chars);
    }

    private static bool HandlesEqual(string? left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsLive(Subscription subscription)
    {
        if (subscription.State is null)
        {
            return true;
        }

        return !TerminalStates.Contains(subscription.State.Value);
    }

    private static SubscriptionPlan MapPlan(Product product)
    {
        return new SubscriptionPlan
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? product.Handle ?? string.Empty,
            Description = product.Description,
            Price = CentsToDollars(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value,
            RequireCreditCard = product.RequireCreditCard ?? false
        };
    }

    private static ShopSubscription MapSubscription(Subscription subscription)
    {
        var priceCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents;
        return new ShopSubscription
        {
            Id = subscription.Id ?? 0,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            Price = priceCents is null ? null : CentsToDollars(priceCents),
            State = subscription.State?.Value,
            NextBillingAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
    }

    private static decimal CentsToDollars(long? cents)
        => (cents ?? 0) / 100m;

    private static (string FirstName, string LastName, string Email) SplitIdentity(string userId)
    {
        var email = userId.Contains('@', StringComparison.Ordinal) ? userId : $"{userId}@eshop.local";
        var local = email.Split('@')[0];
        var parts = local.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var first = parts.Length > 0 ? parts[0] : "Shopper";
        var last = parts.Length > 1 ? parts[1] : "Shopper";
        return (first, last, email);
    }

    private BillingException Translate(Exception ex, string fallback)
    {
        switch (ex)
        {
            case BillingException billing:
                return billing;
            case SdkException<RawError> raw:
                return FromRaw(raw.Error, fallback);
            case JsonException:
                return FromJsonException(fallback);
            case MaxioDuplicateWriteException:
                return new BillingException(502, "The billing provider request completed with an unknown outcome.", ex);
            case HttpRequestException:
                return new BillingException(503, "The billing provider is unreachable.", ex);
            case TaskCanceledException:
                return new BillingException(504, "The billing provider did not respond in time.", ex);
            default:
                _logger.LogWarning("Unexpected billing failure: {Message}", ex.Message);
                return new BillingException(502, fallback, ex);
        }
    }

    private static BillingException TranslateListProducts(SdkException<ListProductsForProductFamilyError> ex)
    {
        if (ex.Error.TryGetString(out var message))
        {
            return new BillingException(404, string.IsNullOrWhiteSpace(message)
                ? "The configured product family was not found."
                : "The configured product family was not found.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw, "Unable to list subscription plans.");
        }

        return new BillingException(502, "Unable to list subscription plans.");
    }

    private BillingException TranslateCreateSubscription(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list))
        {
            var detail = list.Errors is { Count: > 0 } ? string.Join(" ", list.Errors) : null;
            _logger.LogWarning("CreateSubscription was rejected: {Detail}", detail ?? "no error list");
            return new BillingException(422, string.IsNullOrWhiteSpace(detail)
                ? "The subscription could not be created."
                : detail);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            _logger.LogWarning("CreateSubscription failed with HTTP {Status}", (int)raw.StatusCode);
            return FromRaw(raw, "The subscription could not be created.");
        }

        return new BillingException(422, "The subscription could not be created.");
    }

    private static BillingException TranslateFindSubscription(SdkException<FindSubscriptionError> ex)
    {
        if (ex.Error.TryGetNoContent(out var missing))
        {
            return FromRaw(missing, "Subscription not found.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return FromRaw(raw, "Unable to look up the subscription.");
        }

        return new BillingException(502, "Unable to look up the subscription.");
    }

    private static BillingException FromJsonException(string fallback)
    {
        var status = MaxioLastStatus.Current;
        if (status is not null && (int)status.Value >= 400 && (int)status.Value < 500)
        {
            return new BillingException((int)status.Value, "The billing provider rejected the request.");
        }

        return new BillingException(502, "The billing provider returned a response that could not be processed.");
    }

    private static BillingException FromRaw(RawError raw, string fallback)
    {
        var code = (int)raw.StatusCode;
        if (code >= 400 && code < 500)
        {
            return new BillingException(code, fallback);
        }

        if (code == 0)
        {
            return new BillingException(502, fallback);
        }

        return new BillingException(502, fallback);
    }
}
