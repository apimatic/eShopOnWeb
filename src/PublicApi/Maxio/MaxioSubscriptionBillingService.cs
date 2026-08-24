using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSubscriptionBillingService : ISubscriptionBillingService
{
    // States in which a subscription already entitles the shopper; used for the idempotency check.
    private static readonly HashSet<string> ActiveStates = new() { "active", "trialing", "past_due", "on_hold" };

    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionBillingService> _logger;

    public MaxioSubscriptionBillingService(
        IMaxioClient maxioClient,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioSubscriptionBillingService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
        return products.Select(ToPlanDto).ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(ShopperInfo shopper, string productHandle, CancellationToken cancellationToken = default)
    {
        var products = await _maxioClient.ListProductsAsync(_settings.ProductFamilyHandle, cancellationToken);
        if (!products.Any(p => p.Handle == productHandle))
        {
            throw new SubscriptionPlanNotFoundException(productHandle);
        }

        var customer = await EnsureCustomerAsync(shopper, cancellationToken);

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var existing = subscriptions.FirstOrDefault(s =>
            s.Product?.Handle == productHandle &&
            s.State is not null && ActiveStates.Contains(s.State));

        if (existing is not null)
        {
            _logger.LogInformation(
                "Shopper {UserId} already has subscription {SubscriptionId} for plan {ProductHandle}; returning it instead of creating a duplicate.",
                shopper.UserId, existing.Id, productHandle);
            return new SubscribeResult(ToSubscriptionDto(existing), Created: false);
        }

        var created = await _maxioClient.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerReference = shopper.UserId,
                Reference = $"{shopper.UserId}:{productHandle}"
            }
        }, cancellationToken);

        _logger.LogInformation(
            "Created Maxio subscription {SubscriptionId} for shopper {UserId} on plan {ProductHandle}.",
            created.Id, shopper.UserId, productHandle);

        return new SubscribeResult(ToSubscriptionDto(created), Created: true);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(ShopperInfo shopper, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (customer is null)
        {
            return new List<SubscriptionDto>();
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(ToSubscriptionDto).ToList();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ShopperInfo shopper, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await _maxioClient.CreateCustomerAsync(new MaxioCreateCustomerRequest
            {
                Customer = new MaxioCreateCustomer
                {
                    FirstName = shopper.UserName,
                    LastName = "eShopOnWeb",
                    Email = shopper.Email,
                    Reference = shopper.UserId
                }
            }, cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            // Maxio enforces uniqueness of customer.reference, so a 422 here means a concurrent
            // request won the race; the customer now exists and can simply be re-read.
            var customer = await _maxioClient.FindCustomerByReferenceAsync(shopper.UserId, cancellationToken);
            if (customer is not null)
            {
                return customer;
            }

            throw;
        }
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? string.Empty,
        Description = product.Description,
        UnitPrice = product.PriceInCents / 100m,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State ?? string.Empty,
        ProductHandle = subscription.Product?.Handle ?? string.Empty,
        ProductName = subscription.Product?.Name ?? string.Empty,
        UnitPrice = subscription.ProductPriceInCents / 100m,
        ActivatedAt = subscription.ActivatedAt,
        NextBillingAt = subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}
