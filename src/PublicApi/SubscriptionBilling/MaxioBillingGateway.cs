using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed class MaxioBillingGateway : IMaxioBillingGateway
{
    private const int PageSize = 20;
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(25);

    private readonly MaxioAdvancedBilling.MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly MaxioTransportContext _transportContext;
    private readonly ILogger<MaxioBillingGateway> _logger;

    public MaxioBillingGateway(
        MaxioAdvancedBilling.MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        MaxioTransportContext transportContext,
        ILogger<MaxioBillingGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _transportContext = transportContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        await EnsureConfiguredFamilyExistsAsync(cancellationToken);

        var plans = new List<SubscriptionPlanDto>();
        for (var page = 1; ; page++)
        {
            var responses = await ExecuteRawAsync(
                "list products",
                ct => _client.Products.ListProducts(
                    dateField: null,
                    filter: null,
                    endDate: null,
                    endDatetime: null,
                    startDate: null,
                    startDatetime: null,
                    includeArchived: false,
                    include: null,
                    page: page,
                    perPage: PageSize,
                    ct: ct),
                cancellationToken);

            foreach (var response in responses)
            {
                var product = response.Product ?? throw ProtocolFailure("A Maxio product response was incomplete.");
                if (product.ArchivedAt is null &&
                    string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
                {
                    plans.Add(MapPlan(product));
                }
            }

            if (responses.Count < PageSize)
            {
                break;
            }
        }

        return plans.OrderBy(plan => plan.PriceInCents).ThenBy(plan => plan.Name).ToArray();
    }

    public async Task<SubscriptionPlanDto> GetPlanAsync(
        string productHandle,
        CancellationToken cancellationToken)
    {
        var response = await ExecuteRawAsync(
            "read product",
            ct => _client.Products.ReadProductByHandle(productHandle, ct: ct),
            cancellationToken);
        var product = response.Product ?? throw ProtocolFailure("The Maxio product response was incomplete.");

        if (product.ArchivedAt is not null ||
            !string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
        {
            throw new BillingProviderException(
                BillingProviderFailureKind.Rejected,
                "The requested subscription plan is not available.",
                HttpStatusCode.NotFound);
        }

        return MapPlan(product);
    }

    public async Task<BillingCustomer> EnsureCustomerAsync(
        string customerReference,
        BillingCustomerProfile profile,
        CancellationToken cancellationToken)
    {
        var existing = await ReadCustomerOrNullAsync(customerReference, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                Email = profile.Email,
                Reference = customerReference
            }
        };

        try
        {
            var response = await ExecuteCreateCustomerAsync(body, cancellationToken);
            return MapCustomer(response, customerReference);
        }
        catch (BillingProviderException)
        {
            // Maxio guarantees customer-reference uniqueness. A concurrent create or a lost
            // response is reconciled by that reference before the original failure is surfaced.
            try
            {
                var reconciled = await ReadCustomerOrNullAsync(customerReference, cancellationToken);
                if (reconciled is not null)
                {
                    return reconciled;
                }
            }
            catch (BillingProviderException reconciliationFailure)
            {
                _logger.LogWarning(
                    "Maxio customer reconciliation failed after create failure ({FailureKind}).",
                    reconciliationFailure.Kind);
            }

            throw;
        }
    }

    public async Task<SubscriptionDto?> FindSubscriptionAsync(
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        using var budget = CreateBudget(cancellationToken);
        using var operation = _transportContext.BeginOperation(singleSend: false);
        try
        {
            var response = await _client.Subscriptions.FindSubscription(
                reference: subscriptionReference,
                ct: budget.Token);
            var subscription = response.Subscription ??
                throw ProtocolFailure("The Maxio subscription response was incomplete.");
            return MapSubscription(subscription);
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw("find subscription", raw, ex);
            }

            throw ProtocolFailure("Maxio returned an unrecognized subscription lookup error.", ex);
        }
        catch (JsonException ex)
        {
            throw FromJson("find subscription", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unavailable("find subscription", ex);
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var collectionMethod = await ResolveNoPaymentCollectionMethodAsync(cancellationToken);
        var body = new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = customerReference,
                Reference = subscriptionReference,
                PaymentCollectionMethod = collectionMethod
            }
        };

        using var budget = CreateBudget(cancellationToken);
        using var operation = _transportContext.BeginOperation(singleSend: true);
        try
        {
            var response = await _client.Subscriptions.CreateSubscription(body, ct: budget.Token);
            var subscription = response.Subscription ??
                throw ProtocolFailure("The Maxio subscription response was incomplete.");
            var mapped = MapSubscription(subscription);
            if (!string.Equals(mapped.Reference, subscriptionReference, StringComparison.Ordinal) ||
                !string.Equals(mapped.ProductHandle, productHandle, StringComparison.Ordinal))
            {
                throw ProtocolFailure("Maxio returned a subscription that did not match the enrollment intent.");
            }

            return mapped;
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                _logger.LogWarning(
                    "Maxio rejected subscription creation with {ErrorCount} validation error(s): {ValidationErrors}",
                    validation.Errors.Count,
                    FormatProviderMessages(validation.Errors));
                throw new BillingProviderException(
                    BillingProviderFailureKind.Rejected,
                    "Maxio rejected the subscription enrollment.",
                    HttpStatusCode.UnprocessableEntity,
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw("create subscription", raw, ex);
            }

            throw ProtocolFailure("Maxio returned an unrecognized subscription creation error.", ex);
        }
        catch (MaxioDuplicateSendBlockedException ex)
        {
            throw new BillingProviderException(
                BillingProviderFailureKind.OutcomeUnknown,
                "The subscription outcome could not be confirmed.",
                innerException: ex);
        }
        catch (JsonException ex)
        {
            throw FromJson("create subscription", ex, writeOutcomeMayBeUnknown: true);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw new BillingProviderException(
                BillingProviderFailureKind.OutcomeUnknown,
                "The subscription outcome could not be confirmed.",
                innerException: ex);
        }
    }

    private async Task<MaxioAdvancedBilling.Models.Enums.CollectionMethod>
        ResolveNoPaymentCollectionMethodAsync(CancellationToken cancellationToken)
    {
        var response = await ExecuteRawAsync(
            "read site",
            ct => _client.Sites.ReadSite(ct: ct),
            cancellationToken);
        var site = response.Site ?? throw ProtocolFailure("The Maxio site response was incomplete.");

        return site.RelationshipInvoicingEnabled switch
        {
            true => MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance,
            false => MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice,
            null => throw ProtocolFailure(
                "Maxio did not identify the site's billing architecture for payment collection.")
        };
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken)
    {
        var customer = await ReadCustomerOrNullAsync(customerReference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var responses = await ExecuteRawAsync(
            "list customer subscriptions",
            ct => _client.Customers.ListCustomerSubscriptions(customer.Id, ct: ct),
            cancellationToken);

        var subscriptions = new List<SubscriptionDto>();
        foreach (var response in responses)
        {
            var subscription = response.Subscription ??
                throw ProtocolFailure("A Maxio subscription response was incomplete.");
            if (string.Equals(
                subscription.Product?.ProductFamily?.Handle,
                _options.ProductFamilyHandle,
                StringComparison.Ordinal))
            {
                subscriptions.Add(MapSubscription(subscription));
            }
        }

        return subscriptions;
    }

    private async Task EnsureConfiguredFamilyExistsAsync(CancellationToken cancellationToken)
    {
        var families = await ExecuteRawAsync(
            "list product families",
            ct => _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: ct),
            cancellationToken);

        var found = families.Any(response =>
            response.ProductFamily is { ArchivedAt: null } family &&
            string.Equals(family.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal));
        if (!found)
        {
            throw new BillingProviderException(
                BillingProviderFailureKind.Protocol,
                "The configured Maxio product family is unavailable.");
        }
    }

    private async Task<BillingCustomer?> ReadCustomerOrNullAsync(
        string reference,
        CancellationToken cancellationToken)
    {
        using var budget = CreateBudget(cancellationToken);
        using var operation = _transportContext.BeginOperation(singleSend: false);
        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: budget.Token);
            return MapCustomer(response, reference);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw("read customer", ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw FromJson("read customer", ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unavailable("read customer", ex);
        }
    }

    private async Task<CustomerResponse> ExecuteCreateCustomerAsync(
        CreateCustomerRequest body,
        CancellationToken cancellationToken)
    {
        using var budget = CreateBudget(cancellationToken);
        using var operation = _transportContext.BeginOperation(singleSend: false);
        try
        {
            return await _client.Customers.CreateCustomer(body, ct: budget.Token);
        }
        catch (SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _))
            {
                throw new BillingProviderException(
                    BillingProviderFailureKind.Rejected,
                    "Maxio rejected the customer record.",
                    HttpStatusCode.UnprocessableEntity,
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw("create customer", raw, ex);
            }

            throw ProtocolFailure("Maxio returned an unrecognized customer creation error.", ex);
        }
        catch (JsonException ex)
        {
            throw FromJson("create customer", ex, writeOutcomeMayBeUnknown: true);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw new BillingProviderException(
                BillingProviderFailureKind.OutcomeUnknown,
                "The customer outcome could not be confirmed.",
                innerException: ex);
        }
    }

    private async Task<T> ExecuteRawAsync<T>(
        string operationName,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var budget = CreateBudget(cancellationToken);
        using var operation = _transportContext.BeginOperation(singleSend: false);
        try
        {
            return await call(budget.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(operationName, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw FromJson(operationName, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            throw Unavailable(operationName, ex);
        }
    }

    private BillingProviderException FromRaw(string operationName, RawError raw, Exception ex)
    {
        _logger.LogWarning("Maxio {Operation} returned HTTP {StatusCode}.", operationName, (int)raw.StatusCode);
        var isClientError = (int)raw.StatusCode is >= 400 and < 500;
        return new BillingProviderException(
            isClientError ? BillingProviderFailureKind.Rejected : BillingProviderFailureKind.Unavailable,
            isClientError ? "Maxio rejected the billing request." : "Maxio billing is temporarily unavailable.",
            raw.StatusCode,
            ex);
    }

    private BillingProviderException FromJson(
        string operationName,
        JsonException ex,
        bool writeOutcomeMayBeUnknown = false)
    {
        var status = _transportContext.LastStatusCode;
        _logger.LogWarning(
            "Maxio {Operation} returned a response that could not be parsed (HTTP {StatusCode}).",
            operationName,
            status is null ? "unknown" : ((int)status.Value).ToString());

        if (status is not null && (int)status.Value is >= 400 and < 500)
        {
            return new BillingProviderException(
                BillingProviderFailureKind.Rejected,
                "Maxio rejected the billing request.",
                status,
                ex);
        }

        return new BillingProviderException(
            writeOutcomeMayBeUnknown ? BillingProviderFailureKind.OutcomeUnknown : BillingProviderFailureKind.Protocol,
            writeOutcomeMayBeUnknown
                ? "The billing outcome could not be confirmed."
                : "Maxio returned a response that could not be processed.",
            status,
            ex);
    }

    private BillingProviderException Unavailable(string operationName, Exception ex)
    {
        _logger.LogWarning(ex, "Maxio {Operation} was unreachable or timed out.", operationName);
        return new BillingProviderException(
            BillingProviderFailureKind.Unavailable,
            "Maxio billing is temporarily unavailable.",
            innerException: ex);
    }

    private static BillingProviderException ProtocolFailure(string message, Exception? ex = null) =>
        new(BillingProviderFailureKind.Protocol, message, innerException: ex);

    private static bool IsTransportFailure(Exception ex, CancellationToken callerToken) =>
        ex is HttpRequestException ||
        (ex is TaskCanceledException && !callerToken.IsCancellationRequested);

    private static string FormatProviderMessages(IReadOnlyList<string> messages)
    {
        const int maxMessages = 10;
        const int maxMessageLength = 500;

        var safeMessages = messages
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Take(maxMessages)
            .Select(message =>
            {
                var singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
                return singleLine.Length <= maxMessageLength
                    ? singleLine
                    : singleLine[..maxMessageLength];
            });

        return string.Join(" | ", safeMessages);
    }

    private static CancellationTokenSource CreateBudget(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalCallBudget);
        return budget;
    }

    private static BillingCustomer MapCustomer(CustomerResponse response, string expectedReference)
    {
        var customer = response.Customer ?? throw ProtocolFailure("The Maxio customer response was incomplete.");
        if (customer.Id is null ||
            string.IsNullOrWhiteSpace(customer.Reference) ||
            !string.Equals(customer.Reference, expectedReference, StringComparison.Ordinal))
        {
            throw ProtocolFailure("The Maxio customer response did not match the application user.");
        }

        return new BillingCustomer(customer.Id.Value, customer.Reference);
    }

    private static SubscriptionPlanDto MapPlan(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            product.PriceInCents is null ||
            product.Interval is null ||
            product.IntervalUnit is null)
        {
            throw ProtocolFailure("A Maxio product was missing required recurring-plan data.");
        }

        return new SubscriptionPlanDto(
            product.Handle,
            product.Name,
            product.Description,
            product.PriceInCents.Value,
            product.PriceInCents.Value / 100m,
            product.Interval.Value,
            product.IntervalUnit.Value,
            product.ProductPricePointHandle,
            product.ProductPricePointName);
    }

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        var product = subscription.Product;
        if (subscription.Id is null ||
            string.IsNullOrWhiteSpace(subscription.Reference) ||
            product is null ||
            string.IsNullOrWhiteSpace(product.Handle) ||
            string.IsNullOrWhiteSpace(product.Name) ||
            subscription.ProductPriceInCents is null ||
            product.Interval is null ||
            product.IntervalUnit is null ||
            subscription.State is null)
        {
            throw ProtocolFailure("A Maxio subscription was missing required billing data.");
        }

        return new SubscriptionDto(
            subscription.Id.Value,
            subscription.Reference,
            product.Handle,
            product.Name,
            subscription.ProductPriceInCents.Value,
            subscription.ProductPriceInCents.Value / 100m,
            product.Interval.Value,
            product.IntervalUnit.Value,
            subscription.State.Value,
            subscription.CurrentPeriodEndsAt);
    }
}
