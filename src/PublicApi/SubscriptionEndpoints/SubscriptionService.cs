using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionService : ISubscriptionService
{
    private const int PlansCacheDurationMinutes = 5;

    private static readonly string[] RenewingStates = { "active", "trialing", "awaiting_signup", "on_hold" };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMemoryCache _cache;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioClient maxioClient,
        UserManager<ApplicationUser> userManager,
        IMemoryCache cache,
        IOptions<MaxioOptions> maxioOptions,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _cache = cache;
        _options = maxioOptions.Value;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<SubscriptionPlanDto>>> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        var products = await GetFamilyProductsCachedAsync(cancellationToken);
        if (products is null)
        {
            return Result<IReadOnlyList<SubscriptionPlanDto>>.NotFound(
                $"No product family with handle '{_options.ProductFamilyHandle}' exists in Maxio.");
        }

        var plans = products.Select(MapPlan).ToList();
        return Result<IReadOnlyList<SubscriptionPlanDto>>.Success(plans);
    }

    public async Task<Result<SubscriptionCreateResult>> SubscribeAsync(string username, string planHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return Result<SubscriptionCreateResult>.Invalid(new List<ValidationError> { new() { Identifier = nameof(planHandle), ErrorMessage = "A plan handle is required." } });
        }

        var userLock = UserLocks.GetOrAdd(username, _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var products = await GetFamilyProductsCachedAsync(cancellationToken);
            if (products is null)
            {
                return Result<SubscriptionCreateResult>.NotFound(
                    $"No product family with handle '{_options.ProductFamilyHandle}' exists in Maxio.");
            }

            var product = products.FirstOrDefault(p =>
                string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
            if (product is null)
            {
                return Result<SubscriptionCreateResult>.NotFound($"Subscription plan '{planHandle}' was not found.");
            }

            var customerResult = await EnsureMaxioCustomerAsync(username, cancellationToken);
            if (!customerResult.IsSuccess)
            {
                return Result<SubscriptionCreateResult>.Error(customerResult.Errors.First());
            }

            var customer = customerResult.Value;

            var existingSubscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = existingSubscriptions.FirstOrDefault(s =>
                string.Equals(s.Product?.Handle, product.Handle, StringComparison.OrdinalIgnoreCase) &&
                RenewingStates.Contains(s.State, StringComparer.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _logger.LogInformation(
                    "User {Username} already holds subscription {SubscriptionId} for plan {PlanHandle}; returning it instead of creating a duplicate.",
                    username, existing.Id, product.Handle);
                return Result<SubscriptionCreateResult>.Success(new SubscriptionCreateResult
                {
                    Subscription = MapSubscription(existing),
                    Created = false
                });
            }

            var created = await _maxioClient.CreateSubscriptionAsync(
                new CreateMaxioSubscriptionRequest
                {
                    ProductHandle = product.Handle!,
                    CustomerId = customer.Id,
                    PaymentCollectionMethod = "remittance"
                },
                cancellationToken);

            _logger.LogInformation(
                "User {Username} subscribed to plan {PlanHandle}; Maxio subscription {SubscriptionId} created.",
                username, product.Handle, created.Id);

            return Result<SubscriptionCreateResult>.Success(new SubscriptionCreateResult
            {
                Subscription = MapSubscription(created),
                Created = true
            });
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<Result<IReadOnlyList<SubscriptionDetailsDto>>> GetMySubscriptionsAsync(string username, CancellationToken cancellationToken = default)
    {
        var customer = await _maxioClient.FindCustomerByReferenceAsync(username, cancellationToken);
        if (customer is null)
        {
            return Result<IReadOnlyList<SubscriptionDetailsDto>>.Success(
                Array.Empty<SubscriptionDetailsDto>());
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        var mapped = subscriptions.Select(MapSubscription).ToList();
        return Result<IReadOnlyList<SubscriptionDetailsDto>>.Success(mapped);
    }

    private async Task<Result<MaxioCustomer>> EnsureMaxioCustomerAsync(string username, CancellationToken cancellationToken)
    {
        var existing = await _maxioClient.FindCustomerByReferenceAsync(username, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return Result<MaxioCustomer>.NotFound($"User '{username}' was not found.");
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? username : user.Email!;
        var request = new CreateMaxioCustomerRequest
        {
            Reference = username,
            Email = email,
            FirstName = DeriveFirstName(username),
            LastName = "Subscriber",
            Organization = "eShopOnWeb"
        };

        try
        {
            var created = await _maxioClient.CreateCustomerAsync(request, cancellationToken);
            _logger.LogInformation("Maxio customer {CustomerId} created for user {Username}.", created.Id, username);
            return created;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422 && ex.Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase)))
        {
            var raced = await _maxioClient.FindCustomerByReferenceAsync(username, cancellationToken);
            if (raced is not null)
            {
                _logger.LogInformation(
                    "Maxio customer creation for {Username} lost a creation race; using existing customer {CustomerId}.",
                    username, raced.Id);
                return raced;
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<MaxioProduct>?> GetFamilyProductsCachedAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"maxio-plans-{_options.ProductFamilyHandle}";
        var products = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(PlansCacheDurationMinutes);
            var allProducts = await _maxioClient.ListProductsAsync(cancellationToken);
            return (IReadOnlyList<MaxioProduct>?)allProducts
                .Where(p => string.Equals(p.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                .ToList();
        });

        return products;
    }

    private static SubscriptionPlanDto MapPlan(MaxioProduct product) => new()
    {
        Handle = product.Handle ?? string.Empty,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequireCreditCard = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
    };

    private static SubscriptionDetailsDto MapSubscription(MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.Product?.Handle ?? string.Empty,
        PlanName = subscription.Product?.Name ?? string.Empty,
        PriceInCents = subscription.ProductPriceInCents,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CustomerId = subscription.Customer?.Id ?? 0,
        CustomerReference = subscription.Customer?.Reference,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod
    };

    private static string DeriveFirstName(string username)
    {
        var localPart = username.Contains('@') ? username[..username.IndexOf('@')] : username;
        var sanitized = new string(localPart.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(sanitized)
            ? "eShop"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(sanitized);
    }
}
