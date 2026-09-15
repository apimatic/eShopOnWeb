using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserLocks = new(StringComparer.Ordinal);
    private readonly AppIdentityDbContext _identityDb;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMaxioBillingClient _maxio;
    private readonly MaxioOptions _options;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        AppIdentityDbContext identityDb,
        UserManager<ApplicationUser> userManager,
        IMaxioBillingClient maxio,
        IOptions<MaxioOptions> options,
        ILogger<SubscriptionService> logger)
    {
        _identityDb = identityDb;
        _userManager = userManager;
        _maxio = maxio;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        return products
            .Where(product => product.ArchivedAt is null && !string.IsNullOrWhiteSpace(product.Handle))
            .OrderBy(product => product.Name)
            .Select(ToPlanDto)
            .ToArray();
    }

    public async Task<SubscriptionDto> SubscribeAsync(string userName, string requestedPlanHandle, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new InvalidOperationException("The authenticated eShopOnWeb user could not be found.");

        var products = await _maxio.ListProductsAsync(_options.ProductFamilyHandle, cancellationToken);
        var product = products.FirstOrDefault(candidate =>
            candidate.ArchivedAt is null &&
            string.Equals(candidate.Handle, requestedPlanHandle.Trim(), StringComparison.OrdinalIgnoreCase));
        if (product is null || string.IsNullOrWhiteSpace(product.Handle))
        {
            throw new ArgumentException("The requested subscription plan is not available.", nameof(requestedPlanHandle));
        }

        var userLock = UserLocks.GetOrAdd(user.Id, static _ => new SemaphoreSlim(1, 1));
        await userLock.WaitAsync(cancellationToken);
        try
        {
            var customerReference = CustomerReference(user.Id);
            var subscriptionReference = SubscriptionReference(user.Id, product.Handle);
            var customer = await EnsureCustomerAsync(user, customerReference, cancellationToken);

            var existingLink = await _identityDb.MaxioSubscriptionLinks
                .SingleOrDefaultAsync(link => link.UserId == user.Id && link.ProductHandle == product.Handle, cancellationToken);
            var subscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);

            if (existingLink is not null && subscription is null)
            {
                _logger.LogWarning("Maxio subscription link {SubscriptionReference} exists locally but was not found remotely.", subscriptionReference);
                throw new MaxioApiException(502, "verify the existing Maxio subscription");
            }

            if (subscription is null)
            {
                try
                {
                    subscription = await _maxio.CreateSubscriptionAsync(
                        product.Handle,
                        customerReference,
                        subscriptionReference,
                        cancellationToken);
                }
                catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
                {
                    // A concurrent request may have won the create race. The deterministic
                    // reference makes the follow-up lookup recover the existing resource.
                    subscription = await _maxio.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                    if (subscription is null)
                        throw;
                }
            }

            await UpsertLinkAsync(user.Id, product.Handle, customer.Id, subscription, customerReference, subscriptionReference, cancellationToken);
            return ToSubscriptionDto(subscription, product);
        }
        finally
        {
            userLock.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByNameAsync(userName)
            ?? throw new InvalidOperationException("The authenticated eShopOnWeb user could not be found.");
        var customer = await _maxio.FindCustomerByReferenceAsync(CustomerReference(user.Id), cancellationToken);
        if (customer is null)
            return Array.Empty<SubscriptionDto>();

        var subscriptions = await _maxio.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions.Select(subscription => ToSubscriptionDto(subscription, subscription.Product)).ToArray();
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (existing is not null)
            return existing;

        var (firstName, lastName) = CustomerName(user.Email ?? user.UserName ?? "customer@example.com");
        try
        {
            return await _maxio.CreateCustomerAsync(
                reference,
                firstName,
                lastName,
                user.Email ?? user.UserName ?? throw new InvalidOperationException("The authenticated user has no email address."),
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode is 409 or 422)
        {
            // Customer references are unique in Maxio. Recover when another request won.
            var recovered = await _maxio.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (recovered is null)
                throw;
            return recovered;
        }
    }

    private async Task UpsertLinkAsync(
        string userId,
        string productHandle,
        int customerId,
        MaxioSubscription subscription,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var link = await _identityDb.MaxioSubscriptionLinks
            .SingleOrDefaultAsync(item => item.UserId == userId && item.ProductHandle == productHandle, cancellationToken);
        var now = DateTime.UtcNow;
        if (link is null)
        {
            link = new MaxioSubscriptionLink
            {
                UserId = userId,
                ProductHandle = productHandle,
                CreatedAtUtc = now
            };
            await _identityDb.MaxioSubscriptionLinks.AddAsync(link, cancellationToken);
        }

        link.CustomerReference = customerReference;
        link.SubscriptionReference = subscriptionReference;
        link.MaxioCustomerId = customerId;
        link.MaxioSubscriptionId = subscription.Id;
        link.UpdatedAtUtc = now;
        await _identityDb.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle!,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit,
        RequiresPaymentMethod = product.RequireCreditCard,
        Taxable = product.Taxable
    };

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription, MaxioProduct? product) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        PlanHandle = product?.Handle ?? string.Empty,
        PlanName = product?.Name ?? string.Empty,
        PriceInCents = subscription.PriceInCents != 0 ? subscription.PriceInCents : product?.PriceInCents ?? 0,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };

    private static string CustomerReference(string userId) => $"eshop-user:{userId}";

    private static string SubscriptionReference(string userId, string productHandle)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(productHandle))).ToLowerInvariant();
        return $"eshop-subscription:{userId}:{hash[..16]}";
    }

    private static (string FirstName, string LastName) CustomerName(string email)
    {
        var localPart = email.Split('@')[0];
        var pieces = localPart.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return pieces.Length switch
        {
            >= 2 => (pieces[0], string.Join(' ', pieces.Skip(1))),
            1 => (pieces[0], "Customer"),
            _ => ("eShopOnWeb", "Customer")
        };
    }
}
