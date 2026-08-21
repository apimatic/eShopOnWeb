using System;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioWriteOutcomeUnknownException : Exception
{
    public MaxioWriteOutcomeUnknownException(Exception innerException)
        : base("The outcome of the Maxio write is unknown.", innerException)
    {
    }
}

public sealed class MaxioBillingGateway
{
    private const int ProductPageSize = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _settings;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> settings,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(
        CancellationToken cancellationToken)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var plans = new List<SubscriptionPlanDto>();
        var seenHandles = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 1; ; page++)
        {
            IReadOnlyList<ProductResponse> responses;
            try
            {
                responses = await BoundedAsync(
                    ct => _client.ProductFamilies.ListProductsForProductFamily(
                        productFamilyId: familyId.ToString(CultureInfo.InvariantCulture),
                        dateField: null,
                        filter: null,
                        startDate: null,
                        endDate: null,
                        startDatetime: null,
                        endDatetime: null,
                        includeArchived: false,
                        include: null,
                        page: page,
                        perPage: ProductPageSize,
                        ct: ct),
                    cancellationToken);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                {
                    throw SubscriptionBillingException.ContractDrift(ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw TranslateRaw(raw, ex);
                }

                throw SubscriptionBillingException.ProviderUnavailable(ex);
            }
            catch (Exception ex) when (IsReadFailure(ex, cancellationToken))
            {
                throw SubscriptionBillingException.ProviderUnavailable(ex);
            }

            foreach (var response in responses)
            {
                var product = response.Product;
                if (product.ArchivedAt is not null || product.RequireCreditCard is true ||
                    string.IsNullOrWhiteSpace(product.Handle) || !seenHandles.Add(product.Handle) ||
                    string.IsNullOrWhiteSpace(product.Name) || product.PriceInCents is null ||
                    product.Interval is null || product.IntervalUnit is null)
                {
                    continue;
                }

                plans.Add(new SubscriptionPlanDto(
                    product.Handle,
                    product.Name,
                    product.Description,
                    product.PriceInCents.Value,
                    product.Interval.Value,
                    product.IntervalUnit.Value,
                    product.ProductPricePointHandle));
            }

            if (responses.Count < ProductPageSize)
            {
                break;
            }
        }

        return plans;
    }

    public async Task<MaxioProduct> ReadEligibleProductAsync(
        string productHandle,
        CancellationToken cancellationToken)
    {
        ProductResponse response;
        try
        {
            response = await BoundedAsync(
                ct => _client.Products.ReadProductByHandle(productHandle, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            throw SubscriptionBillingException.UnknownPlan();
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, ex);
        }
        catch (Exception ex) when (IsReadFailure(ex, cancellationToken))
        {
            throw SubscriptionBillingException.ProviderUnavailable(ex);
        }

        var product = response.Product;
        if (!string.Equals(product.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw SubscriptionBillingException.UnknownPlan();
        }

        if (product.ArchivedAt is not null || product.RequireCreditCard is true)
        {
            throw SubscriptionBillingException.PlanUnavailable();
        }

        if (string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name))
        {
            throw SubscriptionBillingException.ContractDrift();
        }

        return new MaxioProduct(
            product.Handle,
            product.Name,
            product.Description,
            product.PriceInCents,
            product.Interval,
            product.IntervalUnit?.Value,
            product.ProductPricePointHandle);
    }

    public async Task<MaxioCustomer?> ReadCustomerAsync(
        string customerReference,
        CancellationToken cancellationToken)
    {
        CustomerResponse response;
        try
        {
            response = await BoundedAsync(
                ct => _client.Customers.ReadCustomerByReference(customerReference, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, ex);
        }
        catch (Exception ex) when (IsReadFailure(ex, cancellationToken))
        {
            throw SubscriptionBillingException.ProviderUnavailable(ex);
        }

        return MapCustomer(response);
    }

    public async Task<MaxioCustomer> EnsureCustomerAsync(
        CurrentBillingUser user,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerAsync(user.CustomerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var request = new MaxioAdvancedBilling.Models.CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                Reference = user.CustomerReference
            }
        };

        try
        {
            using var writeScope = MaxioWriteScope.Begin();
            var response = await BoundedAsync(
                ct => _client.Customers.CreateCustomer(request, ct: ct),
                cancellationToken);
            return MapCustomer(response);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                var concurrentWinner = await ReadCustomerAsync(user.CustomerReference, cancellationToken);
                if (concurrentWinner is not null)
                {
                    return concurrentWinner;
                }

                throw SubscriptionBillingException.ProviderValidation(ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, ex);
            }

            throw SubscriptionBillingException.ProviderUnavailable(ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex, cancellationToken))
        {
            _logger.LogWarning("A Maxio customer write had an ambiguous outcome; reconciling by reference.");
            var reconciled = await ReadCustomerAsync(user.CustomerReference, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            throw SubscriptionBillingException.ProviderUnavailable(ex);
        }
        catch (JsonException ex)
        {
            throw SubscriptionBillingException.ProviderUnavailable(ex);
        }
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        MaxioCustomer customer,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionResponse> responses;
        try
        {
            responses = await BoundedAsync(
                ct => _client.Customers.ListCustomerSubscriptions(customer.Id, ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, ex);
        }
        catch (Exception ex) when (IsReadFailure(ex, cancellationToken))
        {
            throw SubscriptionBillingException.ProviderUnavailable(ex);
        }

        var subscriptions = new List<MaxioSubscription>();
        foreach (var response in responses)
        {
            if (response.Subscription is null)
            {
                continue;
            }

            var mapped = TryMapSubscription(response.Subscription);
            if (mapped is not null)
            {
                subscriptions.Add(mapped);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioCustomer customer,
        MaxioProduct product,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var request = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = product.Handle,
                ProductPricePointHandle = product.PricePointHandle,
                CustomerReference = customer.Reference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance
            }
        };

        try
        {
            using var writeScope = MaxioWriteScope.Begin();
            var response = await BoundedAsync(
                ct => _client.Subscriptions.CreateSubscription(request, ct: ct),
                cancellationToken);
            if (response.Subscription is null)
            {
                throw SubscriptionBillingException.ContractDrift();
            }

            return TryMapSubscription(response.Subscription)
                ?? throw SubscriptionBillingException.ContractDrift();
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var errorResponse))
            {
                _logger.LogWarning(
                    "Maxio rejected the subscription create request. Validation errors: {ValidationErrors}",
                    string.Join(" | ", errorResponse.Errors
                        .Take(5)
                        .Select(SanitizeValidationError)));
                throw SubscriptionBillingException.ProviderValidation(ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw TranslateRaw(raw, ex);
            }

            throw SubscriptionBillingException.ProviderUnavailable(ex);
        }
        catch (Exception ex) when (IsAmbiguousWriteFailure(ex, cancellationToken))
        {
            throw new MaxioWriteOutcomeUnknownException(ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioWriteOutcomeUnknownException(ex);
        }
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await BoundedAsync(
                ct => _client.ProductFamilies.ListProductFamilies(
                    dateField: null,
                    startDate: null,
                    endDate: null,
                    startDatetime: null,
                    endDatetime: null,
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateRaw(ex.Error, ex);
        }
        catch (Exception ex) when (IsReadFailure(ex, cancellationToken))
        {
            throw SubscriptionBillingException.ProviderUnavailable(ex);
        }

        var matches = families
            .Select(x => x.ProductFamily)
            .Where(x => x is not null && x.ArchivedAt is null &&
                string.Equals(x.Handle, _settings.ProductFamilyHandle, StringComparison.Ordinal))
            .ToList();

        if (matches.Count != 1 || matches[0]!.Id is null)
        {
            throw SubscriptionBillingException.ContractDrift();
        }

        return matches[0]!.Id!.Value;
    }

    private static MaxioCustomer MapCustomer(CustomerResponse response)
    {
        if (response.Customer.Id is null || string.IsNullOrWhiteSpace(response.Customer.Reference))
        {
            throw SubscriptionBillingException.ContractDrift();
        }

        return new MaxioCustomer(response.Customer.Id.Value, response.Customer.Reference);
    }

    private static MaxioSubscription? TryMapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        if (subscription.Id is null || subscription.State is null || product is null ||
            string.IsNullOrWhiteSpace(product.Handle) || string.IsNullOrWhiteSpace(product.Name))
        {
            return null;
        }

        return new MaxioSubscription(
            subscription.Id.Value,
            subscription.Reference,
            product.Handle,
            product.Name,
            product.ProductFamily?.Handle,
            subscription.ProductPriceInCents,
            subscription.Currency,
            subscription.State.Value,
            subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt);
    }

    private static async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        return await call(budget.Token);
    }

    private static bool IsReadFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or JsonException ||
        exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private static bool IsAmbiguousWriteFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is MaxioWriteReplayBlockedException or HttpRequestException ||
        exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private static string SanitizeValidationError(string error)
    {
        const int maxLength = 256;
        var singleLine = error.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maxLength
            ? singleLine
            : singleLine[..maxLength];
    }

    private static SubscriptionBillingException TranslateRaw(RawError error, Exception innerException)
    {
        return error.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                SubscriptionBillingException.ProviderConfiguration(innerException),
            HttpStatusCode.UnprocessableEntity =>
                SubscriptionBillingException.ProviderValidation(innerException),
            _ => SubscriptionBillingException.ProviderUnavailable(innerException)
        };
    }
}
