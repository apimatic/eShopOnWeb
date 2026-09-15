using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(ClaimsPrincipal principal, string? productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

public sealed class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private const int PageSize = 100;
    private static readonly TimeSpan ProviderCallBudget = TimeSpan.FromSeconds(25);

    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioOptions _options;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserSubscriptionCoordinator _coordinator;

    public MaxioSubscriptionService(
        MaxioAdvancedBillingClient client,
        IOptions<MaxioOptions> options,
        UserManager<ApplicationUser> userManager,
        IUserSubscriptionCoordinator coordinator)
    {
        _client = client;
        _options = options.Value;
        _userManager = userManager;
        _coordinator = coordinator;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var family = await FindProductFamilyAsync(cancellationToken);
        var products = await ListProductsAsync(family, cancellationToken);

        return products
            .Where(item => item.Product?.Handle is not null && item.Product.ArchivedAt is null)
            .Select(item =>
            {
                var product = item.Product!;
                return new SubscriptionPlanDto
                {
                    Handle = product.Handle!,
                    Name = product.Name ?? product.Handle!,
                    Description = product.Description,
                    PriceInCents = product.PriceInCents,
                    Interval = product.Interval,
                    IntervalUnit = product.IntervalUnit?.Value,
                    InitialChargeInCents = product.InitialChargeInCents,
                    TrialPriceInCents = product.TrialPriceInCents,
                    TrialInterval = product.TrialInterval,
                    TrialIntervalUnit = product.TrialIntervalUnit?.Value,
                    RequiresPaymentMethod = product.RequireCreditCard
                };
            })
            .ToList();
    }

    public async Task<SubscriptionDto> SubscribeAsync(
        ClaimsPrincipal principal, string? productHandle, CancellationToken cancellationToken)
    {
        var identity = await ResolveIdentityAsync(principal, cancellationToken);
        var customerReference = References.Customer(identity.Email);
        var subscriptionReference = References.Subscription(identity.Email, productHandle);

        using var lease = await _coordinator.EnterAsync(customerReference, cancellationToken);
        var family = await FindProductFamilyAsync(cancellationToken);
        var product = await FindProductAsync(family, productHandle, cancellationToken);
        var customer = await EnsureCustomerAsync(identity, customerReference, cancellationToken);
        var existing = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
        if (existing?.Subscription is not null)
        {
            return MapSubscription(existing.Subscription);
        }

        try
        {
            var created = await CallProviderAsync(ct => _client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = product.Product!.Handle,
                        CustomerReference = customer.Customer!.Reference ?? customerReference,
                        Reference = subscriptionReference,
                        PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice
                    }
                }, ct: ct), cancellationToken);

            return RequireSubscription(created).Subscription is { } subscription
                ? MapSubscription(subscription)
                : throw new MaxioProviderException("Maxio returned an empty subscription response.");
        }
        catch (SdkException<CreateSubscriptionError> ex)
        {
            var reconciled = await FindSubscriptionAsync(subscriptionReference, cancellationToken);
            if (reconciled?.Subscription is not null)
            {
                return MapSubscription(reconciled.Subscription);
            }

            throw ProviderFromCreateSubscriptionError(ex);
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var identity = await ResolveIdentityAsync(principal, cancellationToken);
        var customerReference = References.Customer(identity.Email);
        var customer = await TryReadCustomerAsync(customerReference, cancellationToken);
        if (customer?.Customer?.Id is not int customerId)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await CallProviderAsync(
            ct => _client.Customers.ListCustomerSubscriptions(customerId, ct: ct), cancellationToken);
        return subscriptions
            .Where(item => item.Subscription is not null)
            .Select(item => MapSubscription(item.Subscription!))
            .ToList();
    }

    private async Task<ProductFamilyResponse> FindProductFamilyAsync(CancellationToken cancellationToken)
    {
        var expectedHandle = RequiredOption(_options.ProductFamilyHandle, nameof(_options.ProductFamilyHandle));
        var families = await CallProviderAsync(ct => _client.ProductFamilies.ListProductFamilies(
            dateField: null,
            startDate: null,
            endDate: null,
            startDatetime: null,
            endDatetime: null,
            ct: ct), cancellationToken);

        var matches = families.Where(item => string.Equals(
            item.ProductFamily?.Handle, expectedHandle, StringComparison.Ordinal)).ToList();
        if (matches.Count != 1 || matches[0].ProductFamily?.Id is null)
        {
            throw new MaxioProviderException("The configured Maxio product family was not found or is ambiguous.");
        }

        return matches[0];
    }

    private async Task<ProductResponse> FindProductAsync(
        ProductFamilyResponse family, string? productHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productHandle))
        {
            throw new MaxioProviderException("A subscription plan handle is required.", HttpStatusCode.BadRequest);
        }

        var products = await ListProductsAsync(family, cancellationToken);

        var product = products.SingleOrDefault(item => item.Product is not null &&
            string.Equals(item.Product.Handle, productHandle, StringComparison.Ordinal) &&
            item.Product.ArchivedAt is null);
        if (product?.Product is null)
        {
            throw new MaxioProviderException("The selected subscription plan was not found.", HttpStatusCode.NotFound);
        }

        return product;
    }

    private async Task<IReadOnlyList<ProductResponse>> ListProductsAsync(
        ProductFamilyResponse family, CancellationToken cancellationToken)
    {
        var products = new List<ProductResponse>();
        for (var page = 1; ; page++)
        {
            var pageProducts = await CallProviderAsync(ct => _client.ProductFamilies.ListProductsForProductFamily(
                productFamilyId: family.ProductFamily!.Id!.Value.ToString(CultureInfo.InvariantCulture),
                dateField: null,
                filter: null,
                startDate: null,
                endDate: null,
                startDatetime: null,
                endDatetime: null,
                includeArchived: false,
                include: null,
                page: page,
                perPage: PageSize,
                ct: ct), cancellationToken);

            if (pageProducts.Count == 0)
            {
                break;
            }

            products.AddRange(pageProducts);
            if (pageProducts.Count < PageSize)
            {
                break;
            }
        }

        return products;
    }

    private async Task<CustomerResponse> EnsureCustomerAsync(
        IdentityData identity, string customerReference, CancellationToken cancellationToken)
    {
        var existing = await TryReadCustomerAsync(customerReference, cancellationToken);
        if (existing?.Customer is not null)
        {
            return existing;
        }

        try
        {
            var created = await CallProviderAsync(ct => _client.Customers.CreateCustomer(
                new CreateCustomerRequest
                {
                    Customer = new CreateCustomer
                    {
                        FirstName = identity.FirstName,
                        LastName = identity.LastName,
                        Email = identity.Email,
                        Reference = customerReference
                    }
                }, ct: ct), cancellationToken);
            return RequireCustomer(created);
        }
        catch (SdkException<CreateCustomerError> ex)
        {
            var reconciled = await TryReadCustomerAsync(customerReference, cancellationToken);
            if (reconciled?.Customer is not null)
            {
                return reconciled;
            }

            throw ProviderFromCreateCustomerError(ex);
        }
    }

    private async Task<CustomerResponse?> TryReadCustomerAsync(
        string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallProviderAsync(
                ct => _client.Customers.ReadCustomerByReference(reference, ct: ct), cancellationToken);
            return RequireCustomer(response);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<SubscriptionResponse?> FindSubscriptionAsync(
        string reference, CancellationToken cancellationToken)
    {
        try
        {
            var response = await CallProviderAsync(
                ct => _client.Subscriptions.FindSubscription(reference, ct: ct), cancellationToken);
            if (response.Subscription is null)
            {
                throw new MaxioProviderException("Maxio returned an empty subscription lookup response.");
            }

            return response;
        }
        catch (SdkException<FindSubscriptionError> ex)
        {
            if (ex.Error.TryGetNoContent(out _))
            {
                return null;
            }

            throw ProviderFromFindSubscriptionError(ex);
        }
    }

    private async Task<IdentityData> ResolveIdentityAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new MaxioProviderException("The authenticated identity does not contain a user name.", HttpStatusCode.Unauthorized);
        }

        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email ?? userName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new MaxioProviderException("The authenticated identity does not contain an email address.", HttpStatusCode.BadRequest);
        }

        var normalized = email.Trim().ToLowerInvariant();
        var localPart = normalized.Split('@')[0];
        var pieces = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = pieces.Length > 0 ? pieces[0] : "eShop";
        var lastName = pieces.Length > 1 ? pieces[1] : "Shopper";
        return new IdentityData(normalized, ToDisplayName(firstName), ToDisplayName(lastName));
    }

    private async Task<T> CallProviderAsync<T>(
        Func<CancellationToken, Task<T>> call, CancellationToken requestCancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        timeout.CancelAfter(ProviderCallBudget);
        try
        {
            return await call(timeout.Token);
        }
        catch (MaxioProviderException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioProviderException("Maxio could not be reached.", null, ex);
        }
        catch (TaskCanceledException ex) when (!requestCancellationToken.IsCancellationRequested)
        {
            throw new MaxioProviderException("Maxio did not respond within the allowed time.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new MaxioProviderException("Maxio returned a response that could not be processed.", null, ex);
        }
    }

    private static SubscriptionResponse RequireSubscription(SubscriptionResponse response)
    {
        if (response.Subscription is null)
        {
            throw new MaxioProviderException("Maxio returned an empty subscription response.");
        }

        return response;
    }

    private static CustomerResponse RequireCustomer(CustomerResponse response)
    {
        if (response.Customer is null)
        {
            throw new MaxioProviderException("Maxio returned an empty customer response.");
        }

        return response;
    }

    private static SubscriptionDto MapSubscription(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            State = subscription.State?.Value,
            PriceInCents = subscription.ProductPriceInCents,
            CurrentBillingAmountInCents = subscription.CurrentBillingAmountInCents,
            NextBillingDate = subscription.NextAssessmentAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
        };
    }

    private static string RequiredOption(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new MaxioProviderException($"Maxio:{name} is not configured.") : value;

    private static MaxioProviderException ProviderFromCreateCustomerError(SdkException<CreateCustomerError> ex)
    {
        if (ex.Error.TryGetCustomerErrorResponse1(out var validation))
        {
            return new MaxioProviderException("Maxio rejected the customer details.", HttpStatusCode.UnprocessableEntity);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new MaxioProviderException("Maxio rejected the customer request.", raw.StatusCode);
        }

        return new MaxioProviderException("Maxio rejected the customer request.");
    }

    private static MaxioProviderException ProviderFromCreateSubscriptionError(SdkException<CreateSubscriptionError> ex)
    {
        if (ex.Error.TryGetErrorListResponse1(out var validation))
        {
            var details = validation.Errors is { Count: > 0 }
                ? $" {string.Join("; ", validation.Errors)}"
                : string.Empty;
            return new MaxioProviderException(
                $"Maxio rejected the subscription request.{details}",
                HttpStatusCode.UnprocessableEntity);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return new MaxioProviderException("Maxio rejected the subscription request.", raw.StatusCode);
        }

        return new MaxioProviderException("Maxio rejected the subscription request.");
    }

    private static MaxioProviderException ProviderFromFindSubscriptionError(SdkException<FindSubscriptionError> ex)
    {
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new MaxioProviderException("Maxio could not read the subscription.", raw.StatusCode);
        }

        return new MaxioProviderException("Maxio could not read the subscription.");
    }

    private static string ToDisplayName(string value) =>
        value.Length == 0 ? "Shopper" : char.ToUpperInvariant(value[0]) + value[1..];

    private sealed record IdentityData(string Email, string FirstName, string LastName);
}

internal static class References
{
    public static string Customer(string email) => $"eshop-customer-{Hash(email)}";
    public static string Subscription(string email, string? productHandle) =>
        $"eshop-subscription-{Hash($"{email}:{productHandle}")}";

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
