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
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainIdentity = Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate.BillingCustomerIdentity;
using DomainPlan = Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate.SubscriptionPlan;
using DomainSubscription = Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate.CustomerSubscription;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Recurring subscriptions backed by Maxio Advanced Billing.
/// </summary>
/// <remarks>
/// Maxio is the system of record: nothing is mirrored into the eShopOnWeb database, and a user is tied to
/// their Maxio customer through a reference derived from the user name rather than a stored mapping. That
/// keeps the integration correct on the in-memory database, which drops every row on restart.
/// </remarks>
public sealed class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    /// <summary>
    /// The states in which a subscription already occupies a plan, so a repeat signup must return it
    /// rather than enroll again. Live and problem states both count — "past due" is still a subscription.
    /// End-of-life states (canceled, expired, suspended, ...) deliberately do not, so a shopper can
    /// resubscribe after cancelling.
    /// </summary>
    private static readonly HashSet<string> ActiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        SubscriptionState.Active.Value,
        SubscriptionState.Trialing.Value,
        SubscriptionState.Assessing.Value,
        SubscriptionState.Pending.Value,
        SubscriptionState.Paused.Value,
        SubscriptionState.PastDue.Value,
        SubscriptionState.SoftFailure.Value,
        SubscriptionState.Unpaid.Value,
        SubscriptionState.AwaitingSignup.Value
    };

    private const int ProductPageSize = 100;
    private const int MaxProductPages = 50;

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSiteProvider _siteProvider;
    private readonly MaxioSubscribeGate _gate;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        MaxioAdvancedBillingClient client,
        MaxioSiteProvider siteProvider,
        MaxioSubscribeGate gate,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _client = client;
        _siteProvider = siteProvider;
        _gate = gate;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DomainPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var budget = CreateBudget(cancellationToken);
        return await LoadPlansAsync(budget.Token, cancellationToken);
    }

    public async Task<DomainSubscription> SubscribeAsync(
        DomainIdentity identity,
        string? planHandle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        using var budget = CreateBudget(cancellationToken);
        var token = budget.Token;

        var plans = await LoadPlansAsync(token, cancellationToken);
        var plan = ResolveTargetPlan(planHandle, plans);

        if (plan.RequiresPaymentMethod)
        {
            // The provider would reject this anyway; saying so up front beats a round-trip and a 422.
            throw new BillingException(
                BillingFailureKind.InvalidRequest,
                $"Plan '{plan.Handle}' requires a payment method, which this API does not collect.");
        }

        var collectionMethod = await ResolveCollectionMethodAsync(token);

        var reference = MaxioCustomerReference.For(identity.UserName, _settings.CustomerReferencePrefix);

        // Two clicks arriving together would otherwise both pass the "is there one already?" check.
        using var gate = await _gate.AcquireAsync(reference, token);

        var customerId = await EnsureCustomerAsync(identity, reference, token, cancellationToken);

        var existing = await FindActiveSubscriptionAsync(customerId, plan.Handle, token, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Customer {CustomerId} is already subscribed to '{PlanHandle}' (subscription {SubscriptionId}); returning it.",
                customerId, plan.Handle, existing.Id);
            return MapSubscription(existing, wasCreated: false);
        }

        return await CreateSubscriptionAsync(customerId, plan.Handle, collectionMethod, token, cancellationToken);
    }

    public async Task<IReadOnlyList<DomainSubscription>> GetSubscriptionsAsync(
        DomainIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        using var budget = CreateBudget(cancellationToken);
        var token = budget.Token;

        var reference = MaxioCustomerReference.For(identity.UserName, _settings.CustomerReferencePrefix);

        var customer = await FindCustomerByReferenceAsync(reference, token, cancellationToken);
        if (customer?.Id is null)
        {
            return Array.Empty<DomainSubscription>();
        }

        var subscriptions = await ListCustomerSubscriptionsAsync(customer.Id.Value, token, cancellationToken);

        return subscriptions
            .Select(subscription => MapSubscription(subscription, wasCreated: false))
            .OrderByDescending(subscription => subscription.Id)
            .ToList();
    }

    // ---------------------------------------------------------------- plans

    private async Task<IReadOnlyList<DomainPlan>> LoadPlansAsync(
        CancellationToken token,
        CancellationToken callerToken)
    {
        var familyId = await ResolveProductFamilyIdAsync(token, callerToken);
        var site = await _siteProvider.GetSiteAsync(token);
        var products = await ListFamilyProductsAsync(familyId, token, callerToken);

        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .Select(product => MapPlan(product, site?.Currency))
            .OrderBy(plan => plan.PriceInCents)
            .ToList();
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken token, CancellationToken callerToken)
    {
        const string operation = "listing product families";
        var configuredHandle = _settings.ProductFamilyHandle?.Trim();

        if (string.IsNullOrWhiteSpace(configuredHandle))
        {
            throw new BillingException(
                BillingFailureKind.NotConfigured,
                $"'{MaxioSettings.ConfigurationSectionName}:{nameof(MaxioSettings.ProductFamilyHandle)}' is not configured.");
        }

        IReadOnlyList<ProductFamilyResponse> families;
        try
        {
            families = await _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: token);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, operation, ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable(operation, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            ThrowIfCallerCancelled(callerToken, ex);
            throw Unreachable(operation, ex);
        }

        var matches = families
            .Select(family => family.ProductFamily)
            .Where(family => family?.Id is not null
                             && string.Equals(family.Handle, configuredHandle, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            throw new BillingException(
                BillingFailureKind.NotConfigured,
                $"No billing product family on this site has the configured handle '{configuredHandle}'.");
        }

        if (matches.Count > 1)
        {
            // Picking one would silently sell from an arbitrary catalogue.
            throw new BillingException(
                BillingFailureKind.NotConfigured,
                $"{matches.Count} billing product families share the configured handle '{configuredHandle}'.");
        }

        return matches[0]!.Id!.Value;
    }

    private async Task<IReadOnlyList<Product>> ListFamilyProductsAsync(
        int familyId,
        CancellationToken token,
        CancellationToken callerToken)
    {
        const string operation = "listing subscription plans";
        var familyIdText = familyId.ToString(CultureInfo.InvariantCulture);
        var products = new List<Product>();

        for (var page = 1; page <= MaxProductPages; page++)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyIdText,
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
                    ct: token);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out var message))
                {
                    _logger.LogWarning(
                        "Maxio could not find product family {FamilyId} while {Operation}: {ProviderMessage}",
                        familyId, operation, message);

                    throw new BillingException(
                        BillingFailureKind.NotConfigured,
                        $"The configured billing product family '{_settings.ProductFamilyHandle}' could not be read.",
                        ex);
                }

                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw Translate(raw, operation, ex);
                }

                throw new BillingException(
                    BillingFailureKind.ProviderError,
                    $"The billing provider returned an unrecognised error while {operation}.",
                    ex);
            }
            catch (JsonException ex)
            {
                // Either a 2xx body that no longer matches the model, or a 404 body that is not the JSON
                // string the error model expects. For a read there is nothing the caller can do about
                // either, and the status is already gone, so both surface the same way.
                throw Unreadable(operation, ex);
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                ThrowIfCallerCancelled(callerToken, ex);
                throw Unreachable(operation, ex);
            }

            products.AddRange(pageItems.Select(item => item.Product));

            if (pageItems.Count < ProductPageSize)
            {
                break;
            }
        }

        return products;
    }

    private DomainPlan ResolveTargetPlan(string? requestedHandle, IReadOnlyList<DomainPlan> plans)
    {
        if (plans.Count == 0)
        {
            throw new BillingException(
                BillingFailureKind.NotConfigured,
                $"The billing product family '{_settings.ProductFamilyHandle}' offers no subscription plans.");
        }

        var handle = string.IsNullOrWhiteSpace(requestedHandle)
            ? _settings.DefaultProductHandle?.Trim()
            : requestedHandle.Trim();

        if (string.IsNullOrWhiteSpace(handle))
        {
            if (plans.Count == 1)
            {
                return plans[0];
            }

            throw new BillingException(
                BillingFailureKind.InvalidRequest,
                "No plan was specified and no default plan is configured.",
                plans.Select(plan => plan.Handle).ToList(),
                null);
        }

        var match = plans.FirstOrDefault(plan =>
            string.Equals(plan.Handle, handle, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            throw new BillingException(
                BillingFailureKind.InvalidRequest,
                $"'{handle}' is not an available subscription plan.",
                plans.Select(plan => plan.Handle).ToList(),
                null);
        }

        return match;
    }

    /// <summary>
    /// Decides how the provider should collect this subscription's balance.
    /// </summary>
    /// <remarks>
    /// Signing up produces an immediate balance, and the default (charge the card on file) fails outright
    /// when there is no card — which is every shopper here, because this API captures no payment details.
    /// Invoicing that balance instead is what makes a card-free enrollment possible. Which invoice-style
    /// value the provider accepts depends on the site's billing architecture, so it is read from the site
    /// rather than assumed; an operator can still pin it through configuration.
    /// </remarks>
    private async Task<CollectionMethod> ResolveCollectionMethodAsync(CancellationToken token)
    {
        if (!MaxioCollectionMethods.TryResolve(_settings.PaymentCollectionMethod, out var configured))
        {
            throw new BillingException(
                BillingFailureKind.NotConfigured,
                $"'{MaxioSettings.ConfigurationSectionName}:{nameof(MaxioSettings.PaymentCollectionMethod)}' is not a value the provider accepts.",
                MaxioCollectionMethods.SupportedValues,
                null);
        }

        if (configured is not null)
        {
            return configured;
        }

        var site = await _siteProvider.GetSiteAsync(token);

        if (site is null)
        {
            // Guessing would either fail the signup or bill it the wrong way; neither is acceptable.
            throw new BillingException(
                BillingFailureKind.ProviderUnavailable,
                "The billing provider's site settings could not be read, so the subscription was not created.");
        }

        return site.RelationshipInvoicingEnabled
            ? CollectionMethod.Remittance
            : CollectionMethod.Invoice;
    }

    // ------------------------------------------------------------ customers

    private async Task<int> EnsureCustomerAsync(
        DomainIdentity identity,
        string reference,
        CancellationToken token,
        CancellationToken callerToken)
    {
        var existing = await FindCustomerByReferenceAsync(reference, token, callerToken);
        if (existing?.Id is not null)
        {
            return existing.Id.Value;
        }

        var created = await CreateCustomerAsync(identity, reference, token, callerToken);
        if (created?.Id is null)
        {
            throw new BillingException(
                BillingFailureKind.ProviderError,
                "The billing provider created a customer but did not report its identifier.");
        }

        _logger.LogInformation(
            "Created Maxio customer {CustomerId} for reference {CustomerReference}.", created.Id, reference);

        return created.Id.Value;
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken token,
        CancellationToken callerToken)
    {
        const string operation = "looking up the billing customer";

        try
        {
            var response = await _client.Customers.ReadCustomerByReference(reference, ct: token);
            return response.Customer;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, operation, ex);
        }
        catch (JsonException ex)
        {
            // "I could not read the answer" is not "there is no such customer". Treating it as an absence
            // here would turn a corrupt response into a duplicate customer, so fail instead.
            throw Unreadable(operation, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            ThrowIfCallerCancelled(callerToken, ex);
            throw Unreachable(operation, ex);
        }
    }

    private async Task<Customer?> CreateCustomerAsync(
        DomainIdentity identity,
        string reference,
        CancellationToken token,
        CancellationToken callerToken)
    {
        const string operation = "creating the billing customer";

        var email = identity.Email
                    ?? (identity.UserName.Contains('@', StringComparison.Ordinal) ? identity.UserName : null);

        if (email is null)
        {
            throw new BillingException(
                BillingFailureKind.InvalidRequest,
                "This account has no email address, which the billing provider requires to create a customer.");
        }

        var (firstName, lastName) = DeriveName(email);

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

        try
        {
            using var singleSend = MaxioSingleSendScope.Begin();
            var response = await _client.Customers.CreateCustomer(body, ct: token);
            return response.Customer;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
            {
                // The provider enforces uniqueness on the reference, so the overwhelmingly likely cause of
                // a rejection here is that a concurrent request created the customer first. Re-read and
                // use the winner; only fail if there really is nothing there.
                var raced = await FindCustomerByReferenceAsync(reference, token, callerToken);
                if (raced?.Id is not null)
                {
                    return raced;
                }

                var details = ReadCustomerValidationDetails(validation);
                throw new BillingException(
                    BillingFailureKind.InvalidRequest,
                    "The billing provider rejected the customer details.",
                    details,
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, operation, ex);
            }

            throw new BillingException(
                BillingFailureKind.ProviderError,
                $"The billing provider returned an unrecognised error while {operation}.",
                ex);
        }
        catch (MaxioDuplicateSendBlockedException ex)
        {
            return await ReconcileCustomerAsync(reference, operation, callerToken, ex);
        }
        catch (JsonException ex)
        {
            return await ReconcileCustomerAsync(reference, operation, callerToken, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            ThrowIfCallerCancelled(callerToken, ex);
            return await ReconcileCustomerAsync(reference, operation, callerToken, ex);
        }
    }

    /// <summary>
    /// Settles a create whose outcome we could not read, by asking the provider what actually exists.
    /// </summary>
    private async Task<Customer?> ReconcileCustomerAsync(
        string reference,
        string operation,
        CancellationToken callerToken,
        Exception cause)
    {
        _logger.LogWarning(
            cause, "Outcome of {Operation} for reference {CustomerReference} is unknown; reconciling.",
            operation, reference);

        if (!callerToken.IsCancellationRequested)
        {
            using var reconcile = CreateReconcileBudget(callerToken);

            var customer = await FindCustomerByReferenceAsync(reference, reconcile.Token, callerToken);
            if (customer?.Id is not null)
            {
                return customer;
            }
        }

        throw new BillingException(
            BillingFailureKind.IndeterminateOutcome,
            $"The outcome of {operation} could not be established.",
            cause);
    }

    private static IReadOnlyList<string> ReadCustomerValidationDetails(CustomerErrorResponse1 validation)
    {
        // The generated 422 payload for this operation carries only these two collections, and both are
        // optional — so it deserializes cleanly and can still yield nothing at all. Never assume it has text.
        var details = new List<string>();

        if (validation.Errors?.PerPage is { Count: > 0 } perPage)
        {
            details.AddRange(perPage);
        }

        if (validation.Errors?.PricePoint is { Count: > 0 } pricePoint)
        {
            details.AddRange(pricePoint);
        }

        if (details.Count == 0)
        {
            details.Add("A customer with this reference may already exist.");
        }

        return details;
    }

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        // eShopOnWeb accounts carry no given/family name, but the provider requires both. Derive something
        // stable and readable from the address rather than sending an empty string.
        var local = email.Split('@', 2)[0];
        var tokens = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            return ("eShopOnWeb", "Customer");
        }

        var firstName = Capitalize(tokens[0]);
        var lastName = tokens.Length > 1
            ? string.Join(' ', tokens.Skip(1).Select(Capitalize))
            : "Customer";

        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length <= 1 ? value.ToUpperInvariant() : char.ToUpperInvariant(value[0]) + value[1..];

    // -------------------------------------------------------- subscriptions

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(
        int customerId,
        CancellationToken token,
        CancellationToken callerToken)
    {
        const string operation = "listing the customer's subscriptions";

        try
        {
            var response = await _client.Customers.ListCustomerSubscriptions(customerId, ct: token);

            return response
                .Select(item => item.Subscription)
                .Where(subscription => subscription is not null)
                .Select(subscription => subscription!)
                .ToList();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex.Error, operation, ex);
        }
        catch (JsonException ex)
        {
            throw Unreadable(operation, ex);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            ThrowIfCallerCancelled(callerToken, ex);
            throw Unreachable(operation, ex);
        }
    }

    private async Task<Subscription?> FindActiveSubscriptionAsync(
        int customerId,
        string planHandle,
        CancellationToken token,
        CancellationToken callerToken)
    {
        var subscriptions = await ListCustomerSubscriptionsAsync(customerId, token, callerToken);

        return subscriptions
            .Where(subscription =>
                string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase)
                && IsActive(subscription.State))
            .OrderByDescending(subscription => subscription.Id ?? 0)
            .FirstOrDefault();
    }

    private async Task<DomainSubscription> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        CollectionMethod collectionMethod,
        CancellationToken token,
        CancellationToken callerToken)
    {
        const string operation = "creating the subscription";

        var body = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = planHandle,
                CustomerId = customerId,

                // Without this the provider tries to charge the signup balance and rejects the whole call
                // with "No payment method was on file" — even for a plan whose own require_credit_card is
                // false. Invoicing the balance instead is what makes a card-free enrollment possible.
                PaymentCollectionMethod = collectionMethod
            }
        };

        try
        {
            using var singleSend = MaxioSingleSendScope.Begin();
            var response = await _client.Subscriptions.CreateSubscription(body, ct: token);

            if (response.Subscription is null)
            {
                throw new BillingException(
                    BillingFailureKind.ProviderError,
                    "The billing provider accepted the subscription but did not return it.");
            }

            _logger.LogInformation(
                "Created Maxio subscription {SubscriptionId} on '{PlanHandle}' for customer {CustomerId}.",
                response.Subscription.Id, planHandle, customerId);

            return MapSubscription(response.Subscription, wasCreated: true);
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out var validation))
            {
                _logger.LogWarning(
                    "Maxio rejected the subscription for customer {CustomerId} on '{PlanHandle}': {ProviderErrors}",
                    customerId, planHandle, string.Join("; ", validation.Errors));

                throw new BillingException(
                    BillingFailureKind.InvalidRequest,
                    "The billing provider rejected the subscription request.",
                    validation.Errors,
                    ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(raw, operation, ex);
            }

            throw new BillingException(
                BillingFailureKind.ProviderError,
                $"The billing provider returned an unrecognised error while {operation}.",
                ex);
        }
        catch (MaxioDuplicateSendBlockedException ex)
        {
            return await ReconcileSubscriptionAsync(customerId, planHandle, callerToken, ex, wasRejection: false);
        }
        catch (JsonException ex)
        {
            // Ambiguous by construction: either the subscription was created and its body was unreadable,
            // or it was rejected with a 422 whose shape did not match. Asking the provider what exists
            // settles it — and if nothing exists, it was a rejection, not an outage.
            return await ReconcileSubscriptionAsync(customerId, planHandle, callerToken, ex, wasRejection: true);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            ThrowIfCallerCancelled(callerToken, ex);
            return await ReconcileSubscriptionAsync(customerId, planHandle, callerToken, ex, wasRejection: false);
        }
    }

    private async Task<DomainSubscription> ReconcileSubscriptionAsync(
        int customerId,
        string planHandle,
        CancellationToken callerToken,
        Exception cause,
        bool wasRejection)
    {
        _logger.LogWarning(
            cause,
            "Outcome of creating a subscription on '{PlanHandle}' for customer {CustomerId} is unknown; reconciling.",
            planHandle, customerId);

        if (!callerToken.IsCancellationRequested)
        {
            using var reconcile = CreateReconcileBudget(callerToken);

            var existing = await FindActiveSubscriptionAsync(
                customerId, planHandle, reconcile.Token, callerToken);

            if (existing is not null)
            {
                // The write did land; report it as an existing subscription rather than a new one.
                return MapSubscription(existing, wasCreated: false);
            }
        }

        if (wasRejection)
        {
            throw new BillingException(
                BillingFailureKind.InvalidRequest,
                "The billing provider rejected the subscription request.",
                cause);
        }

        throw new BillingException(
            BillingFailureKind.IndeterminateOutcome,
            "The subscription request could not be completed and its outcome could not be established.",
            cause);
    }

    // ------------------------------------------------------------- mapping

    private static bool IsActive(SubscriptionState? state) =>
        state is not null && ActiveStates.Contains(state.Value);

    private static DomainPlan MapPlan(Product product, string? currency) => new()
    {
        Handle = product.Handle!,
        Name = string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
        Description = product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Currency = currency,
        Interval = product.Interval ?? 0,
        IntervalUnit = product.IntervalUnit?.Value,
        HasTrial = (product.TrialInterval ?? 0) > 0,
        TrialPriceInCents = product.TrialPriceInCents,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit?.Value,
        SetupFeeInCents = product.InitialChargeInCents ?? 0,
        RequiresPaymentMethod = product.RequireCreditCard ?? false
    };

    private static DomainSubscription MapSubscription(Subscription subscription, bool wasCreated) => new()
    {
        Id = subscription.Id ?? 0,
        State = subscription.State?.Value ?? "unknown",
        IsActive = IsActive(subscription.State),
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
        Currency = subscription.Currency,
        CustomerId = subscription.Customer?.Id,
        CustomerReference = subscription.Customer?.Reference,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        TrialEndsAt = subscription.TrialEndedAt,
        CanceledAt = subscription.CanceledAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod?.Value,
        WasCreatedByThisRequest = wasCreated
    };

    // ------------------------------------------------ budgets and failures

    /// <summary>
    /// One budget per public operation, linked to the caller's token. The SDK's own timeouts bound a
    /// single attempt; this is the only bound on the whole call, which is what the caller experiences.
    /// </summary>
    private CancellationTokenSource CreateBudget(CancellationToken callerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(_settings.CallBudget);
        return cts;
    }

    /// <summary>A fresh, short budget for reconciliation — the operation's own budget may be spent.</summary>
    private CancellationTokenSource CreateReconcileBudget(CancellationToken callerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(_settings.RequestTimeout);
        return cts;
    }

    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    /// <summary>The caller gave up (or disconnected) — that is cancellation, not a provider failure.</summary>
    private static void ThrowIfCallerCancelled(CancellationToken callerToken, Exception ex)
    {
        if (callerToken.IsCancellationRequested && ex is OperationCanceledException)
        {
            throw ex;
        }
    }

    private BillingException Translate(RawError raw, string operation, Exception inner)
    {
        var status = raw.StatusCode;

        string body;
        try
        {
            body = raw.ReadAsString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the Maxio error body while {Operation}.", operation);
            body = string.Empty;
        }

        _logger.LogWarning(
            "Maxio returned HTTP {StatusCode} while {Operation}. Body: {ProviderBody}",
            (int)status, operation, body);

        var kind = MapStatus(status);

        // Deliberately does not echo the provider body: the caller gets the kind of failure and the
        // status, and the detail stays in the log.
        var message = kind switch
        {
            BillingFailureKind.Unauthorized =>
                $"The billing provider rejected this application's credentials while {operation}.",
            BillingFailureKind.NotFound =>
                $"The billing provider has no matching record while {operation}.",
            BillingFailureKind.RateLimited =>
                $"The billing provider is rate limiting requests; please retry shortly ({operation}).",
            BillingFailureKind.InvalidRequest =>
                $"The billing provider rejected the request while {operation}.",
            BillingFailureKind.Conflict =>
                $"The billing provider reported a conflict while {operation}.",
            BillingFailureKind.ProviderUnavailable =>
                $"The billing provider is temporarily unavailable ({operation}).",
            _ =>
                $"The billing provider returned an unexpected response (HTTP {(int)status}) while {operation}."
        };

        return new BillingException(kind, message, inner);
    }

    private static BillingFailureKind MapStatus(HttpStatusCode status) => (int)status switch
    {
        400 or 422 => BillingFailureKind.InvalidRequest,
        401 or 403 => BillingFailureKind.Unauthorized,
        404 => BillingFailureKind.NotFound,
        409 => BillingFailureKind.Conflict,
        429 => BillingFailureKind.RateLimited,
        >= 500 => BillingFailureKind.ProviderUnavailable,
        _ => BillingFailureKind.ProviderError
    };

    private static BillingException Unreachable(string operation, Exception inner) => new(
        BillingFailureKind.ProviderUnavailable,
        $"The billing provider could not be reached in time while {operation}.",
        inner);

    private static BillingException Unreadable(string operation, Exception inner) => new(
        BillingFailureKind.ProviderError,
        $"The billing provider returned a response that could not be processed while {operation}.",
        inner);
}
