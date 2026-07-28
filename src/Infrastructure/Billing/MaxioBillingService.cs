using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// <see cref="ISubscriptionBillingService"/> backed by the Maxio Advanced Billing SDK. This is the
/// only type that talks to the SDK; every SDK/provider failure is translated into a single
/// <see cref="BillingException"/> carrying a caller-safe message and the HTTP status to surface.
/// </summary>
public class MaxioBillingService : ISubscriptionBillingService
{
    private const string DefaultCurrency = "USD";

    // Subscription-state wire values that mean the subscription is live and occupies the plan,
    // so a re-subscribe returns it instead of creating a duplicate. Values per the SDK contract.
    private static readonly HashSet<string> LiveStateValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "assessing", "past_due", "soft_failure", "paused", "on_hold",
    };

    // Serializes a single subscriber's subscribe operations across the process so a double-click
    // (two near-simultaneous requests hitting this instance) cannot both pass the duplicate guard
    // and create two subscriptions. Customer duplication is separately prevented by Maxio's
    // 'reference' uniqueness; this closes the subscription-create race.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new();

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(cancellationToken);
        var products = await ListProductsAsync(familyId, cancellationToken);
        return products.Select(MapPlan).ToList();
    }

    public async Task<CustomerSubscription> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default)
    {
        if (subscriber is null || string.IsNullOrWhiteSpace(subscriber.Reference))
            throw new BillingException("A subscriber identity is required.", HttpStatusCode.BadRequest);

        var handle = string.IsNullOrWhiteSpace(planHandle) ? _settings.DefaultPlanHandle : planHandle;
        if (string.IsNullOrWhiteSpace(handle))
            throw new BillingException("No plan was specified and no default plan is configured.", HttpStatusCode.BadRequest);

        // Validate the plan exists in the configured family — a clean 404 beats a raw provider 422.
        var plan = await ResolvePlanAsync(handle!, cancellationToken);
        if (plan is null)
            throw new BillingException($"Plan '{handle}' was not found.", HttpStatusCode.NotFound);

        var gate = SubscribeLocks.GetOrAdd(subscriber.Reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Idempotent customer ensure (read-by-reference, create only if absent).
            var customerId = await EnsureCustomerAsync(subscriber, cancellationToken);

            // Duplicate-active guard: return an existing live subscription for the same plan.
            var existing = await FindLiveSubscriptionAsync(customerId, plan.Handle, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Existing live subscription {SubscriptionId} found for customer {CustomerId} on plan {Plan}; returning it.",
                    existing.Id, customerId, plan.Handle);
                return MapSubscription(existing, alreadyExisted: true);
            }

            var created = await CreateSubscriptionAsync(plan.Handle, customerId, cancellationToken);
            _logger.LogInformation(
                "Created subscription {SubscriptionId} for customer {CustomerId} on plan {Plan}.",
                created.Id, customerId, plan.Handle);
            return MapSubscription(created, alreadyExisted: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        string subscriberReference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subscriberReference))
            throw new BillingException("A subscriber reference is required.", HttpStatusCode.BadRequest);

        var customerId = await TryGetCustomerIdAsync(subscriberReference, cancellationToken);
        if (customerId is null)
            return Array.Empty<CustomerSubscription>();

        var subs = await ListCustomerSubscriptionsAsync(customerId.Value, cancellationToken);
        return subs.Select(s => MapSubscription(s, alreadyExisted: false)).ToList();
    }

    // ---- Plans -----------------------------------------------------------------------------

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct)
    {
        var handle = _settings.ProductFamilyHandle;
        if (string.IsNullOrWhiteSpace(handle))
            throw new BillingException("The billing product family is not configured.", HttpStatusCode.InternalServerError);

        var families = await RunAsync(
            () => _client.ProductFamilies.ListProductFamilies(
                dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, ct: ct),
            "loading product families",
            ct);

        var id = families
            .FirstOrDefault(f => string.Equals(f.ProductFamily?.Handle, handle, StringComparison.OrdinalIgnoreCase))
            ?.ProductFamily?.Id;

        if (id is null)
            throw new BillingException($"The configured product family '{handle}' was not found.", HttpStatusCode.InternalServerError);

        return id.Value;
    }

    private async Task<IReadOnlyList<Product>> ListProductsAsync(int familyId, CancellationToken ct)
    {
        var products = new List<Product>();
        const int perPage = 100;
        var page = 1;

        while (true)
        {
            IReadOnlyList<ProductResponse> pageItems;
            try
            {
                pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: familyId.ToString(),
                    dateField: null, filter: null, startDate: null, endDate: null,
                    startDatetime: null, endDatetime: null, includeArchived: null, include: null,
                    page: page, perPage: perPage, ct: ct);
            }
            catch (SdkException<ListProductsForProductFamilyError> ex)
            {
                if (ex.Error.TryGetString(out _))
                    throw new BillingException("The billing product family was not found.", HttpStatusCode.InternalServerError, ex);
                if (ex.Error.TryGetRawError(out var raw))
                    throw MapRawError(raw, "listing products");
                throw new BillingException("The billing provider could not list products.", HttpStatusCode.BadGateway, ex);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { throw Translate(ex, "listing products", ct); }

            products.AddRange(pageItems.Select(r => r.Product));
            if (pageItems.Count < perPage)
                break;
            page++;
        }

        return products;
    }

    private async Task<SubscriptionPlan?> ResolvePlanAsync(string handle, CancellationToken ct)
    {
        var plans = await GetPlansAsync(ct);
        return plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.OrdinalIgnoreCase));
    }

    // ---- Customer --------------------------------------------------------------------------

    /// <summary>Returns the customer id for the reference, or null when no such customer exists (404).</summary>
    private async Task<int?> TryGetCustomerIdAsync(string reference, CancellationToken ct)
    {
        try
        {
            var resp = await _client.Customers.ReadCustomerByReference(reference, ct);
            return resp.Customer.Id;
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            // A genuine miss — signalled by the 404 status, NOT by an unreadable body.
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw Translate(ex, "looking up the customer", ct); }
    }

    /// <summary>Ensures a customer exists for the subscriber (idempotent read-then-create).</summary>
    private async Task<int> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken ct)
    {
        var existing = await TryGetCustomerIdAsync(subscriber.Reference, ct);
        if (existing is not null)
            return existing.Value;

        var (firstName, lastName) = DeriveName(subscriber.Email);
        var body = new CreateCustomerRequest
        {
            Customer = new CreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = subscriber.Email,
                Reference = subscriber.Reference,
            },
        };

        try
        {
            var resp = await _client.Customers.CreateCustomer(body, ct);
            var id = resp.Customer.Id
                ?? throw new BillingException("The billing provider did not return a customer id.", HttpStatusCode.BadGateway);
            _logger.LogInformation("Created Maxio customer {CustomerId} for reference {Reference}.", id, subscriber.Reference);
            return id;
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            // 'reference' is unique server-side, so a concurrent create (double-click) lands here.
            // Reconcile by re-reading: if the customer now exists, the create raced and we use it.
            var raced = await TryGetCustomerIdAsync(subscriber.Reference, ct);
            if (raced is not null)
                return raced.Value;

            throw new BillingException(
                $"The billing provider could not create the customer.{DescribeCustomerError(ex.Error)}",
                HttpStatusCode.UnprocessableEntity, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw Translate(ex, "creating the customer", ct); }
    }

    // ---- Subscriptions ---------------------------------------------------------------------

    private async Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken ct)
    {
        var responses = await RunAsync(
            () => _client.Customers.ListCustomerSubscriptions(customerId, ct),
            "listing subscriptions",
            ct);

        return responses.Where(r => r.Subscription is not null).Select(r => r.Subscription!).ToList();
    }

    private async Task<Subscription?> FindLiveSubscriptionAsync(int customerId, string planHandle, CancellationToken ct)
    {
        var subs = await ListCustomerSubscriptionsAsync(customerId, ct);
        return subs.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase) && IsLive(s.State));
    }

    private async Task<Subscription> CreateSubscriptionAsync(string planHandle, int customerId, CancellationToken ct)
    {
        // The plans require no card. A default (automatic) collection method attempts an immediate
        // card charge and fails "no payment method on file", so we bill by invoice instead. Ordered
        // strategies cover both site architectures and net-terms requirements without parsing messages:
        //   1. remittance — Relationship Invoicing sites: issue an invoice for the balance
        //   2. remittance + net terms due-on-receipt — sites that require net terms
        //   3. invoice — legacy Statements sites, which reject 'remittance'
        // Each attempt only runs after a prior 422 (a validation rejection creates nothing, so
        // retrying with different terms cannot create a duplicate). Transport failures are not retried
        // here — they fall through to Translate.
        var strategies = new (CollectionMethod Method, string? NetTerms)[]
        {
            (CollectionMethod.Remittance, null),
            (CollectionMethod.Remittance, "0"),
            (CollectionMethod.Invoice, null),
        };

        SdkException<CreateSubscriptionError>? lastValidationError = null;

        foreach (var (method, netTerms) in strategies)
        {
            var body = new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = method,
                    NetTerms = netTerms,
                },
            };

            try
            {
                var resp = await _client.Subscriptions.CreateSubscription(body, ct);
                return resp.Subscription
                    ?? throw new BillingException("The billing provider did not return a subscription.", HttpStatusCode.BadGateway);
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                lastValidationError = ex;
                // Validation rejection — try the next collection strategy.
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { throw Translate(ex, "creating the subscription", ct); }
        }

        throw new BillingException(
            $"The subscription could not be created.{DescribeSubscriptionError(lastValidationError!.Error)}",
            HttpStatusCode.UnprocessableEntity, lastValidationError);
    }

    // ---- Mapping ---------------------------------------------------------------------------

    private static SubscriptionPlan MapPlan(Product p)
    {
        var cents = p.PriceInCents ?? 0;
        return new SubscriptionPlan
        {
            ProductId = p.Id,
            Handle = p.Handle ?? string.Empty,
            Name = p.Name ?? string.Empty,
            Description = p.Description,
            PriceInCents = cents,
            Price = cents / 100m,
            Currency = DefaultCurrency,
            Interval = p.Interval ?? 0,
            IntervalUnit = p.IntervalUnit?.Value ?? string.Empty,
        };
    }

    private static CustomerSubscription MapSubscription(Subscription s, bool alreadyExisted)
    {
        var cents = s.ProductPriceInCents ?? s.Product?.PriceInCents ?? 0;
        return new CustomerSubscription
        {
            SubscriptionId = s.Id,
            PlanHandle = s.Product?.Handle ?? string.Empty,
            PlanName = s.Product?.Name ?? string.Empty,
            PriceInCents = cents,
            Price = cents / 100m,
            Currency = DefaultCurrency,
            State = s.State?.Value ?? string.Empty,
            CurrentPeriodStartedAt = s.CurrentPeriodStartedAt,
            NextBillingDate = s.CurrentPeriodEndsAt,
            AlreadyExisted = alreadyExisted,
        };
    }

    private static bool IsLive(SubscriptionState? state)
    {
        var value = state?.Value;
        return value is not null && LiveStateValues.Contains(value);
    }

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var local = email;
        var at = email.IndexOf('@');
        if (at > 0)
            local = email[..at];
        if (string.IsNullOrWhiteSpace(local))
            local = "eShopOnWeb";
        // eShopOnWeb identities carry no name; supply safe placeholders for Maxio's required fields.
        return (local, "eShopOnWeb Subscriber");
    }

    // ---- Error boundary --------------------------------------------------------------------

    private async Task<T> RunAsync<T>(Func<Task<T>> operation, string action, CancellationToken ct)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw Translate(ex, action, ct); }
    }

    /// <summary>
    /// Translates an SDK/transport failure into a <see cref="BillingException"/>. Cancellation
    /// triggered by the caller's token is expected to be handled (rethrown) at the call site
    /// before this runs.
    /// </summary>
    private static BillingException Translate(Exception ex, string action, CancellationToken ct)
    {
        return ex switch
        {
            BillingException be => be,
            SdkException<RawError> raw => MapRawError(raw.Error, action),
            // A success (2xx) body that no longer matches the model, OR an error body that does not
            // match its generated error shape: either way the response was unprocessable → 502.
            System.Text.Json.JsonException => new BillingException(
                $"The billing provider returned a response for {action} that could not be processed.",
                HttpStatusCode.BadGateway, ex),
            // Transport failures (unreachable host, reset, per-attempt timeout) — provider unavailable.
            HttpRequestException or TaskCanceledException => new BillingException(
                "The billing provider is currently unavailable. Please try again shortly.",
                HttpStatusCode.ServiceUnavailable, ex),
            _ => new BillingException($"An unexpected error occurred while {action}.", HttpStatusCode.BadGateway, ex),
        };
    }

    private static BillingException MapRawError(RawError error, string action)
    {
        var code = (int)error.StatusCode;
        // A provider 4xx is actionable by the caller — surface the same status; everything else is 502.
        var status = code is >= 400 and < 500 ? (HttpStatusCode)code : HttpStatusCode.BadGateway;
        return new BillingException($"The billing provider rejected the request while {action}.", status);
    }

    private static string DescribeCustomerError(CreateCustomerError error)
    {
        if (error.TryGetCustomerErrorResponse1(out var body) && body?.Errors is not null)
        {
            var messages = new List<string>();
            if (body.Errors.PerPage is not null)
                messages.AddRange(body.Errors.PerPage);
            if (body.Errors.PricePoint is not null)
                messages.AddRange(body.Errors.PricePoint);
            if (messages.Count > 0)
                return " " + string.Join("; ", messages);
        }
        return string.Empty;
    }

    private static string DescribeSubscriptionError(CreateSubscriptionError error)
    {
        if (error.TryGetErrorListResponse1(out var body) && body?.Errors is { Count: > 0 })
            return " " + string.Join("; ", body.Errors);
        return string.Empty;
    }
}
