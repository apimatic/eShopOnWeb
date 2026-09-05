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
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    // Wire values (SubscriptionState) that should NOT block a shopper from subscribing to the
    // same plan again - see maxio-plan.md §2.5.
    private static readonly HashSet<string> TerminalSubscriptionStates =
        new(StringComparer.OrdinalIgnoreCase) { "canceled", "expired" };

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;

    // Maxio confirms no server-side uniqueness enforcement on subscription create (maxio-plan.md
    // Blockers). This in-process gate is the mitigation available to a single-instance app: it
    // serializes the check-then-act "does this customer already have this plan" sequence below so a
    // double-click can't interleave two creates. It does not protect against a race across multiple
    // app instances.
    private readonly SemaphoreSlim _subscribeGate = new(1, 1);

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken ct = default)
    {
        var products = await ListPlansForFamilyAsync(ct);
        return products
            .Select(p => p.Product)
            .Select(p => new SubscriptionPlanDto(
                p.Handle ?? string.Empty,
                p.Name ?? string.Empty,
                p.PriceInCents,
                p.IntervalUnit?.Value,
                p.Interval))
            .ToList();
    }

    public async Task<IReadOnlyList<CustomerSubscriptionDto>> GetSubscriptionsForCustomerAsync(MaxioCustomerIdentity customer, CancellationToken ct = default)
    {
        var customerId = await FindOrCreateCustomerAsync(customer, ct);
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);
        return subscriptions.Select(MapSubscription).ToList();
    }

    public async Task<CustomerSubscriptionDto> SubscribeAsync(MaxioCustomerIdentity customer, string planHandle, CancellationToken ct = default)
    {
        await _subscribeGate.WaitAsync(ct);
        try
        {
            var customerId = await FindOrCreateCustomerAsync(customer, ct);
            var existingSubscriptions = await ListCustomerSubscriptionsAsync(customerId, ct);

            var existing = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Subscription?.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) &&
                !TerminalSubscriptionStates.Contains(s.Subscription?.State?.Value ?? string.Empty));
            if (existing?.Subscription is not null)
            {
                return MapSubscription(existing);
            }

            var created = await CreateSubscriptionAsync(customerId, planHandle, ct);
            return MapSubscription(created);
        }
        finally
        {
            _subscribeGate.Release();
        }
    }

    private static CustomerSubscriptionDto MapSubscription(SubscriptionResponse response)
    {
        var subscription = response.Subscription;
        long? priceInCents = subscription?.CurrentBillingAmountInCents ?? subscription?.Product?.PriceInCents;
        return new CustomerSubscriptionDto(
            subscription?.Id ?? 0,
            subscription?.Product?.Handle,
            subscription?.Product?.Name,
            priceInCents,
            subscription?.State?.Value,
            subscription?.NextAssessmentAt);
    }

    private async Task<IReadOnlyList<ProductResponse>> ListPlansForFamilyAsync(CancellationToken ct)
    {
        try
        {
            return await _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: $"handle:{_options.ProductFamilyHandle}",
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: null,
                include: null,
                page: 1,
                perPage: 200,
                ct: ct);
        }
        catch (SdkException<ListProductsForProductFamilyError> ex)
        {
            // The typed overload can't take a handle directly (maxio-plan.md §5 Blockers); "handle:x"
            // works as the path segment, but fall back to resolving the numeric id if it ever 404s.
            if (ex.Error.TryGetString(out _))
            {
                var familyId = await ResolveProductFamilyIdByHandleAsync(ct);
                return await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(),
                    dateField: null,
                    filter: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    includeArchived: null,
                    include: null,
                    page: 1,
                    perPage: 200,
                    ct: ct);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioIntegrationException($"Could not list plans for product family '{_options.ProductFamilyHandle}'.", (int)raw.StatusCode, ex);
            }
            throw new MaxioIntegrationException($"Could not list plans for product family '{_options.ProductFamilyHandle}'.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException("Maxio returned a response that could not be processed while listing plans.", null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new MaxioIntegrationException("Maxio was unreachable while listing plans.", null, ex);
        }
    }

    private async Task<int> ResolveProductFamilyIdByHandleAsync(CancellationToken ct)
    {
        try
        {
            var families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct);
            var match = families.FirstOrDefault(f =>
                string.Equals(f.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));
            if (match?.ProductFamily?.Id is int id)
            {
                return id;
            }
            throw new MaxioIntegrationException($"No Maxio product family with handle '{_options.ProductFamilyHandle}' was found.", 404);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioIntegrationException($"Could not resolve product family '{_options.ProductFamilyHandle}'.", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException("Maxio returned a response that could not be processed while resolving the product family.", null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new MaxioIntegrationException("Maxio was unreachable while resolving the product family.", null, ex);
        }
    }

    private async Task<int> FindOrCreateCustomerAsync(MaxioCustomerIdentity customer, CancellationToken ct)
    {
        var existingId = await TryReadCustomerByReferenceAsync(customer.Reference, ct);
        if (existingId is int id)
        {
            return id;
        }

        try
        {
            var response = await _client.Customers.CreateCustomer(new CreateCustomerRequest
            {
                Customer = new CreateCustomer
                {
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Email = customer.Email,
                    Reference = customer.Reference
                }
            }, ct: ct);

            if (response.Customer.Id is int createdId)
            {
                return createdId;
            }
            throw new MaxioIntegrationException($"Maxio created a customer for '{customer.Reference}' but returned no id.");
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // CreateCustomer enforces one customer per reference, but the typed 422 payload never
            // names the conflicting field (maxio-plan.md §2.4) - treat any 422 as a possible
            // concurrent duplicate and re-check before failing.
            var retriedId = await TryReadCustomerByReferenceAsync(customer.Reference, ct);
            if (retriedId is int rid)
            {
                return rid;
            }

            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new MaxioIntegrationException($"Maxio rejected the customer for '{customer.Reference}'.", 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioIntegrationException($"Could not create Maxio customer for '{customer.Reference}'.", (int)raw.StatusCode, ex);
            }
            throw new MaxioIntegrationException($"Could not create Maxio customer for '{customer.Reference}'.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException("Maxio returned a response that could not be processed while creating the customer.", null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new MaxioIntegrationException("Maxio was unreachable while creating the customer.", null, ex);
        }
    }

    private async Task<int?> TryReadCustomerByReferenceAsync(string reference, CancellationToken ct)
    {
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: ct);
            return response.Customer.Id;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioIntegrationException($"Could not look up Maxio customer '{reference}'.", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException("Maxio returned a response that could not be processed while looking up the customer.", null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new MaxioIntegrationException("Maxio was unreachable while looking up the customer.", null, ex);
        }
    }

    private async Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        try
        {
            return await _client.Customers.ListCustomerSubscriptions(customerId, ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MaxioIntegrationException($"Could not list subscriptions for Maxio customer {customerId}.", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException("Maxio returned a response that could not be processed while listing subscriptions.", null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new MaxioIntegrationException("Maxio was unreachable while listing subscriptions.", null, ex);
        }
    }

    private async Task<SubscriptionResponse> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        try
        {
            return await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    // A nonzero-price, no-trial product always tries to charge immediately unless told
                    // otherwise - confirmed live against cp-exp-2, independent of the product's
                    // "require_credit_card" flag (that flag only gates Maxio's hosted checkout UI, not
                    // this server-side charge attempt). DeferSignup creates the subscription in
                    // AwaitingSignup instead of attempting a charge, which is what "payment method not
                    // required" has to mean for a plan configured this way.
                    DeferSignup = true
                }
            }, ct: ct);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errors))
            {
                var message = errors.Errors is { Count: > 0 } ? string.Join("; ", errors.Errors) : "validation failed";
                throw new MaxioIntegrationException($"Maxio rejected the subscription to '{planHandle}': {message}", 422, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new MaxioIntegrationException($"Could not subscribe customer {customerId} to '{planHandle}'.", (int)raw.StatusCode, ex);
            }
            throw new MaxioIntegrationException($"Could not subscribe customer {customerId} to '{planHandle}'.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioIntegrationException("Maxio returned a response that could not be processed while creating the subscription.", null, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, ct))
        {
            throw new MaxioIntegrationException("Maxio was unreachable while creating the subscription.", null, ex);
        }
    }

    private static bool IsTransportFailure(Exception ex, CancellationToken ct) =>
        (ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested;
}
