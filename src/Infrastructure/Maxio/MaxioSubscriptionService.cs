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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Subscription states that count as "already subscribed to this plan" for the idempotent
    // subscribe check. A subscription that ended (Canceled/Expired/FailedToCreate) does not
    // block a fresh subscribe.
    private static readonly HashSet<SubscriptionState> BlockingStates = new()
    {
        SubscriptionState.Pending,
        SubscriptionState.Trialing,
        SubscriptionState.Assessing,
        SubscriptionState.Active,
        SubscriptionState.SoftFailure,
        SubscriptionState.PastDue,
        SubscriptionState.Suspended,
        SubscriptionState.Unpaid,
        SubscriptionState.TrialEnded,
        SubscriptionState.OnHold,
        SubscriptionState.AwaitingSignup,
        SubscriptionState.Paused,
    };

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly Lazy<Task<string?>> _siteCurrency;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
        _siteCurrency = new Lazy<Task<string?>>(() => FetchSiteCurrencyAsync(CancellationToken.None));
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        return await BoundedAsync(async token =>
        {
            var family = await ResolveProductFamilyAsync(token);
            var products = await ListProductsForFamilyAsync(family.Id!.Value, token);
            var currency = await _siteCurrency.Value;

            return (IReadOnlyList<SubscriptionPlan>)products
                .Where(p => p.Product?.ArchivedAt is null)
                .Select(p => MapPlan(p.Product!, currency))
                .ToList();
        }, ct);
    }

    public async Task<CustomerSubscription> SubscribeAsync(string userReference, string planHandle, CancellationToken ct = default)
    {
        return await BoundedAsync(async token =>
        {
            var family = await ResolveProductFamilyAsync(token);
            var products = await ListProductsForFamilyAsync(family.Id!.Value, token);
            var product = products
                .Select(p => p.Product)
                .FirstOrDefault(p => p is not null && p.ArchivedAt is null &&
                    string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));

            if (product is null)
            {
                throw new MaxioServiceException($"No subscription plan with handle '{planHandle}' was found.", HttpStatusCode.NotFound);
            }

            var customer = await FindOrCreateCustomerAsync(userReference, token);
            return await CreateOrReuseSubscriptionAsync(customer.Id!.Value, product, token);
        }, ct);
    }

    public async Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        return await BoundedAsync(async token =>
        {
            var customer = await TryReadCustomerByReferenceAsync(userReference, token);
            if (customer is null)
            {
                return (IReadOnlyList<CustomerSubscription>)Array.Empty<CustomerSubscription>();
            }

            var subscriptions = await _client.Customers.ListCustomerSubscriptions(customer.Id!.Value, token);
            return subscriptions
                .Where(s => s.Subscription is not null)
                .Select(s => MapSubscription(s.Subscription!))
                .ToList();
        }, ct);
    }

    private async Task<ProductFamily> ResolveProductFamilyAsync(CancellationToken ct)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(null, null, null, null, null, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException("Unable to list Maxio product families.", ex);
        }

        var family = families
            .Select(f => f.ProductFamily)
            .FirstOrDefault(f => f is not null &&
                string.Equals(f.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family?.Id is null)
        {
            throw new MaxioServiceException(
                $"Configured Maxio product family handle '{_options.ProductFamilyHandle}' was not found on this site.",
                HttpStatusCode.BadGateway);
        }

        return family;
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsForFamilyAsync(int productFamilyId, CancellationToken ct)
    {
        var results = new List<ProductResponse>();
        var page = 1;
        const int perPage = 100;

        try
        {
            while (true)
            {
                var pageResults = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: productFamilyId.ToString(),
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
                    ct: ct);

                results.AddRange(pageResults);
                if (pageResults.Count < perPage)
                {
                    break;
                }

                page++;
            }
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out var notFound))
            {
                throw new MaxioServiceException($"Maxio product family {productFamilyId} was not found: {notFound}", HttpStatusCode.BadGateway, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException("Unable to list plans for the configured Maxio product family.", raw, ex);
            }

            throw;
        }

        return results;
    }

    private async Task<string?> FetchSiteCurrencyAsync(CancellationToken ct)
    {
        try
        {
            var site = await _client.Sites.ReadSite(ct: ct);
            return site.Site?.Currency;
        }
        catch (SdkException<RawError>)
        {
            // Currency is a display nicety for plan listing; don't fail the whole call over it.
            return null;
        }
    }

    private async Task<Customer> FindOrCreateCustomerAsync(string reference, CancellationToken ct)
    {
        var existing = await TryReadCustomerByReferenceAsync(reference, ct);
        if (existing is not null)
        {
            return existing;
        }

        var (firstName, lastName) = SplitDisplayName(reference);

        try
        {
            var created = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = reference,
                    Reference = reference,
                },
            }, ct: ct);

            if (created.Customer is null)
            {
                throw new MaxioServiceException("Maxio returned no customer body after create.", HttpStatusCode.BadGateway);
            }

            return created.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // A 422 here may be a genuine validation failure, or a benign race where a concurrent
            // request already created the customer for this reference. Re-resolve by reference
            // before surfacing the error - Maxio enforces reference uniqueness, so a successful
            // re-read after a 422 means the race case applies.
            var recovered = await TryReadCustomerByReferenceAsync(reference, ct);
            if (recovered is not null)
            {
                return recovered;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new MaxioServiceException($"Maxio rejected the new customer for '{reference}'.", HttpStatusCode.BadRequest, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException("Unable to create the Maxio customer.", raw, ex);
            }

            throw;
        }
    }

    private async Task<Customer?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer;
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            throw ToProviderException($"Unable to look up the Maxio customer for '{reference}'.", ex);
        }
    }

    private async Task<CustomerSubscription> CreateOrReuseSubscriptionAsync(int customerId, Product product, CancellationToken ct)
    {
        var existing = await FindBlockingSubscriptionAsync(customerId, product.Handle!, ct);
        if (existing is not null)
        {
            return MapSubscription(existing);
        }

        try
        {
            var created = await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    CustomerId = customerId,
                    ProductHandle = product.Handle,
                    // "Invoice" is this (legacy Statements Architecture) site's no-card-required
                    // collection method; the SDK's implicit default ("automatic") requires one.
                    PaymentCollectionMethod = CollectionMethod.Invoice,
                },
            }, ct: ct);

            if (created.Subscription is null)
            {
                throw new MaxioServiceException("Maxio returned no subscription body after create.", HttpStatusCode.BadGateway);
            }

            return MapSubscription(created.Subscription);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            // Same defensive re-check as customer creation: a 422 here might mean a concurrent
            // request already created the subscription we were about to create.
            var recovered = await FindBlockingSubscriptionAsync(customerId, product.Handle!, ct);
            if (recovered is not null)
            {
                return MapSubscription(recovered);
            }

            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var detail = errors.Errors is { Count: > 0 } ? string.Join("; ", errors.Errors) : "validation failed";
                throw new MaxioServiceException($"Maxio rejected the subscription: {detail}", HttpStatusCode.BadRequest, ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw ToProviderException("Unable to create the Maxio subscription.", raw, ex);
            }

            throw;
        }
    }

    private async Task<Subscription?> FindBlockingSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        IReadOnlyList<SubscriptionResponse> subscriptions;
        try
        {
            subscriptions = await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException("Unable to list existing Maxio subscriptions for this customer.", ex);
        }

        return subscriptions
            .Select(s => s.Subscription)
            .FirstOrDefault(s => s is not null &&
                string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                s.State is not null && BlockingStates.Contains(s.State));
    }

    private static SubscriptionPlan MapPlan(Product product, string? currency) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        PriceInCents = product.PriceInCents ?? 0,
        Currency = currency,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value ?? string.Empty,
    };

    private static CustomerSubscription MapSubscription(Subscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        Currency = subscription.Currency,
        State = subscription.State?.Value ?? string.Empty,
        NextBillingDate = subscription.NextAssessmentAt,
    };

    private static (string FirstName, string LastName) SplitDisplayName(string reference)
    {
        var localPart = reference.Contains('@') ? reference[..reference.IndexOf('@')] : reference;
        var separatorIndex = localPart.IndexOfAny(new[] { '.', '_', '-' });

        if (separatorIndex > 0 && separatorIndex < localPart.Length - 1)
        {
            return (localPart[..separatorIndex], localPart[(separatorIndex + 1)..]);
        }

        return (string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart, "Customer");
    }

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (JsonException ex)
        {
            throw new MaxioServiceException("The Maxio API returned a response that could not be processed.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new MaxioServiceException("The Maxio API was unreachable or timed out.", HttpStatusCode.BadGateway, ex);
        }
    }

    private static MaxioServiceException ToProviderException(string message, SdkException<RawError> ex) =>
        ToProviderException(message, ex.Error, ex);

    private static MaxioServiceException ToProviderException(string message, RawError raw, Exception inner)
    {
        var statusCode = (int)raw.StatusCode >= 400 && (int)raw.StatusCode < 500 ? raw.StatusCode : HttpStatusCode.BadGateway;
        return new MaxioServiceException(message, statusCode, inner);
    }
}
