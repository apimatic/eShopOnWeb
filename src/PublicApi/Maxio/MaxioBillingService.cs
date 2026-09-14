using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Maxio Advanced Billing-backed implementation of the subscription billing
/// boundary. Maxio is the system of record: the eShopOnWeb user (its JWT username,
/// which is the user's email) is linked to a Maxio customer by a deterministic,
/// unique customer reference, so no local persistence is required and the
/// integration stays correct across process restarts.
/// </summary>
public sealed class MaxioBillingService : IMaxioBillingService
{
    private const string CustomerReferencePrefix = "eshopweb-user";
    private const string SubscriptionReferencePrefix = "eshopweb-sub";
    private const int MaxPlanPages = 20;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(MaxioAdvancedBillingClient client, IOptions<MaxioOptions> options, ILogger<MaxioBillingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = await FindProductFamilyAsync(cancellationToken);
        if (family?.ProductFamily?.Id is null)
        {
            throw new MaxioBillingException(HttpStatusCode.NotFound,
                $"No billing product family with handle '{_options.ProductFamilyHandle}' was found.");
        }

        var products = await CallAsync(async token =>
        {
            var all = new List<ProductResponse>();
            const int perPage = 50;
            for (var page = 1; page <= MaxPlanPages; page++)
            {
                var pageItems = await _client.ProductFamilies.ListProductsForProductFamily(
                    productFamilyId: family.ProductFamily.Id.Value.ToString(),
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
                    ct: token);
                all.AddRange(pageItems);
                if (pageItems.Count < perPage)
                {
                    break;
                }
            }
            return all;
        }, cancellationToken);

        return products
            .Where(p => p.Product?.ArchivedAt is null)
            .Select(p => MapPlan(p.Product!))
            .ToList();
    }

    public async Task<SubscriptionDetailsDto> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new MaxioBillingException(HttpStatusCode.Unauthorized, "An authenticated user is required to subscribe.");
        }
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioBillingException(HttpStatusCode.BadRequest, "A productHandle identifying the plan to subscribe to is required.");
        }

        var customer = await EnsureCustomerAsync(userName, cancellationToken);

        var subscriptionReference = SubscriptionReference(userName, productHandle);
        var existing = await FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing?.Subscription is not null && !IsTerminated(existing.Subscription.State))
        {
            _logger.LogInformation("User {User} is already subscribed to {Plan}; returning existing subscription {SubscriptionId}.",
                userName, productHandle, existing.Subscription.Id);
            return MapSubscription(existing.Subscription);
        }

        var reference = existing?.Subscription is null
            ? subscriptionReference
            : $"{subscriptionReference}.{DateTimeOffset.UtcNow.UtcTicks}";

        var created = await CallAsync(async token =>
        {
            try
            {
                return (await _client.Subscriptions.CreateSubscription(new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = productHandle,
                        CustomerId = customer.Id,
                        Reference = reference,
                        // Card-less enrollment: the flow never captures a payment method, so
                        // bill via remittance (Relationship Invoicing) instead of automatic
                        // collection, which demands a payment profile for paid products.
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                }, token)).Subscription;
            }
            catch (SdkException<CreateSubscriptionError> ex)
            {
                throw MapCreateSubscriptionError(ex);
            }
        }, cancellationToken);

        if (created is null)
        {
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing system returned an unreadable subscription confirmation.");
        }

        _logger.LogInformation("User {User} subscribed to plan {Plan} as subscription {SubscriptionId}.",
            userName, productHandle, created.Id);
        return MapSubscription(created);
    }

    public async Task<IReadOnlyList<SubscriptionDetailsDto>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new MaxioBillingException(HttpStatusCode.Unauthorized, "An authenticated user is required to read subscriptions.");
        }

        var customer = await EnsureCustomerAsync(userName, cancellationToken);
        if (customer.Id is null)
        {
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing system returned a customer without an identifier.");
        }

        var subscriptions = await CallAsync(token =>
            _client.Customers.ListCustomerSubscriptions(customer.Id.Value, token), cancellationToken);

        return subscriptions
            .Where(s => s.Subscription is not null)
            .Select(s => MapSubscription(s.Subscription!))
            .ToList();
    }

    private async Task<ProductFamilyResponse> FindProductFamilyAsync(CancellationToken cancellationToken)
    {
        var families = await CallAsync(token =>
            _client.ProductFamilies.ListProductFamilies(
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                ct: token), cancellationToken);

        return families.FirstOrDefault(f =>
                string.Equals(f.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            ?? throw new MaxioBillingException(HttpStatusCode.NotFound,
                $"No billing product family with handle '{_options.ProductFamilyHandle}' was found.");
    }

    private async Task<Customer> EnsureCustomerAsync(string userName, CancellationToken cancellationToken)
    {
        var reference = CustomerReference(userName);

        var byReference = await FindCustomerByReferenceAsync(reference, cancellationToken);
        if (byReference is not null)
        {
            return byReference;
        }

        var byEmail = await FindCustomerByEmailAsync(userName, cancellationToken);
        if (byEmail is not null)
        {
            return byEmail;
        }

        try
        {
            return await CreateCustomerAsync(userName, reference, cancellationToken);
        }
        catch (MaxioBillingException ex) when ((int)ex.Status == 422)
        {
            var raced = await FindCustomerByReferenceAsync(reference, cancellationToken)
                        ?? await FindCustomerByEmailAsync(userName, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation("Customer creation for {User} raced an existing record; returning the existing customer.", userName);
                return raced;
            }
            throw;
        }
    }

    private async Task<Customer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await CallAsync(async token =>
                (await _client.Customers.ReadCustomerByReference(reference, token)).Customer, cancellationToken);
        }
        catch (MaxioBillingException ex) when (ex.Status == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Customer?> FindCustomerByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var matches = await CallAsync(token =>
            _client.Customers.ListCustomers(
                direction: null,
                dateField: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                q: email,
                page: 1,
                perPage: 50,
                ct: token), cancellationToken);

        return matches.FirstOrDefault(c =>
            string.Equals(c.Customer?.Email, email, StringComparison.OrdinalIgnoreCase))?.Customer;
    }

    private async Task<Customer> CreateCustomerAsync(string userName, string reference, CancellationToken cancellationToken)
    {
        var (firstName, lastName) = DeriveNames(userName);
        return await CallAsync(async token =>
        {
            try
            {
                return (await _client.Customers.CreateCustomer(new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = userName,
                        Reference = reference
                    }
                }, token)).Customer;
            }
            catch (SdkException<CreateCustomerError> ex)
            {
                throw MapCreateCustomerError(ex);
            }
        }, cancellationToken);
    }

    private async Task<SubscriptionResponse?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        return await CallAsync(async token =>
        {
            try
            {
                return await _client.Subscriptions.FindSubscription(reference, token);
            }
            catch (SdkException<FindSubscriptionError> ex)
            {
                if (ex.Error.TryGetNoContent(out _))
                {
                    return null;
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw MapRawError("subscription lookup", raw);
                }
                throw new MaxioBillingException(HttpStatusCode.BadGateway,
                    "The billing system returned an unreadable subscription lookup response.", ex);
            }
        }, cancellationToken);
    }

    private MaxioBillingException MapCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var list) && list is not null)
        {
            var message = list.Errors is { Count: > 0 }
                ? string.Join("; ", list.Errors)
                : "The billing system rejected the subscription request.";
            return new MaxioBillingException(HttpStatusCode.UnprocessableEntity, message, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError("subscription creation", raw, ex);
        }
        return new MaxioBillingException(HttpStatusCode.UnprocessableEntity,
            "The billing system rejected the subscription request.", ex);
    }

    private MaxioBillingException MapCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var customerError) && customerError?.Errors is not null)
        {
            var messages = new[] { customerError.Errors.PerPage, customerError.Errors.PricePoint }
                .Where(list => list is not null)
                .SelectMany(list => list!)
                .ToList();
            var message = messages.Count > 0
                ? string.Join("; ", messages)
                : "The billing system rejected the customer request.";
            return new MaxioBillingException(HttpStatusCode.UnprocessableEntity, message, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRawError("customer creation", raw, ex);
        }
        return new MaxioBillingException(HttpStatusCode.UnprocessableEntity,
            "The billing system rejected the customer request.", ex);
    }

    private MaxioBillingException MapRawError(string operation, RawError raw, Exception? inner = null)
    {
        var body = Summarize(raw.ReadAsString());
        _logger.LogWarning("Maxio call failed during {Operation} with status {Status}: {Body}",
            operation, (int)raw.StatusCode, body);
        return new MaxioBillingException(raw.StatusCode,
            $"The billing system rejected the {operation} request (HTTP {(int)raw.StatusCode}).", inner);
    }

    private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(CallBudget);
        try
        {
            return await call(budget.Token);
        }
        catch (MaxioBillingException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw MapRawError("Maxio call", ex.Error, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Maxio returned a body that could not be parsed.");
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing system returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Maxio could not be reached.");
            throw new MaxioBillingException(HttpStatusCode.BadGateway,
                "The billing system could not be reached.", ex);
        }
    }

    private static SubscriptionPlanDto MapPlan(Product product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id ?? 0,
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents ?? 0,
            Price = (product.PriceInCents ?? 0) / 100m,
            Currency = null,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit?.Value,
            ProductFamilyHandle = product.ProductFamily?.Handle
        };
    }

    private static SubscriptionDetailsDto MapSubscription(Subscription subscription)
    {
        return new SubscriptionDetailsDto
        {
            SubscriptionId = subscription.Id ?? 0,
            Reference = subscription.Reference,
            CustomerId = subscription.Customer?.Id ?? 0,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            Price = (subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0) / 100m,
            Currency = subscription.Currency,
            State = subscription.State?.Value ?? string.Empty,
            NextBillingDateUtc = subscription.CurrentPeriodEndsAt,
            ActivatedAtUtc = subscription.ActivatedAt
        };
    }

    private static bool IsTerminated(SubscriptionState? state) =>
        state == SubscriptionState.Canceled || state == SubscriptionState.Expired;

    private static string CustomerReference(string userName) =>
        $"{CustomerReferencePrefix}:{userName}";

    private static string SubscriptionReference(string userName, string productHandle) =>
        $"{SubscriptionReferencePrefix}:{userName}:{productHandle}";

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var segments = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return ("eShop", "Shopper");
        }
        var firstName = Capitalize(segments[0]);
        var lastName = segments.Length > 1 ? Capitalize(string.Join(" ", segments.Skip(1))) : "Shopper";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string Summarize(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }
        var flattened = new StringBuilder(body.Length);
        foreach (var ch in body.Replace("\r", "").Replace("\n", " "))
        {
            flattened.Append(ch);
        }
        var text = flattened.ToString();
        const int maxLength = 300;
        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }
}
