using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioSubscriptionService
{
    private readonly IMaxioBillingClient _maxio;
    private readonly AppIdentityDbContext _identityDbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SubscriptionOperationLock _operationLock;

    public MaxioSubscriptionService(
        IMaxioBillingClient maxio,
        AppIdentityDbContext identityDbContext,
        UserManager<ApplicationUser> userManager,
        SubscriptionOperationLock operationLock)
    {
        _maxio = maxio;
        _identityDbContext = identityDbContext;
        _userManager = userManager;
        _operationLock = operationLock;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _maxio.ListProductsAsync(cancellationToken);
        return products
            .Where(product => !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt == null)
            .OrderBy(product => product.Name)
            .Select(product => new SubscriptionPlanDto
            {
                Handle = product.Handle!,
                Name = product.Name ?? product.Handle!,
                Description = product.Description ?? string.Empty,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit ?? string.Empty
            })
            .ToList();
    }

    public async Task<SubscriptionOperationResult?> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        if (user == null || string.IsNullOrWhiteSpace(planHandle))
        {
            return null;
        }

        var normalizedPlanHandle = planHandle.Trim();
        using var operation = await _operationLock.AcquireAsync($"{user.Id}:{normalizedPlanHandle}", cancellationToken);

        var existingMapping = await _identityDbContext.MaxioSubscriptionMappings
            .AsNoTracking()
            .SingleOrDefaultAsync(mapping => mapping.UserId == user.Id && mapping.ProductHandle == normalizedPlanHandle, cancellationToken);
        if (existingMapping != null)
        {
            return new SubscriptionOperationResult(
                ToDto(await _maxio.GetSubscriptionAsync(existingMapping.MaxioSubscriptionId, cancellationToken), normalizedPlanHandle),
                false);
        }

        var plan = (await _maxio.ListProductsAsync(cancellationToken))
            .SingleOrDefault(product => string.Equals(product.Handle, normalizedPlanHandle, StringComparison.OrdinalIgnoreCase) && product.ArchivedAt == null);
        if (plan == null || string.IsNullOrWhiteSpace(plan.Handle))
        {
            return null;
        }

        var externalIdentity = user.UserName ?? user.Id;
        var customerReference = CustomerReferenceFor(externalIdentity);
        var customer = await GetOrCreateCustomerAsync(user, customerReference, cancellationToken);
        var subscriptionReference = SubscriptionReferenceFor(externalIdentity, plan.Handle);
        MaxioSubscription subscription;
        try
        {
            subscription = await _maxio.CreateSubscriptionAsync(
                plan.Handle,
                customerReference,
                subscriptionReference,
                StableToken($"subscription:{subscriptionReference}"),
                cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 409)
        {
            var existing = await FindExistingSubscriptionAsync(customerReference, subscriptionReference, plan.Handle, cancellationToken);
            if (existing == null)
            {
                throw;
            }

            subscription = existing;
        }
        catch (HttpRequestException)
        {
            var existing = await FindExistingSubscriptionAsync(customerReference, subscriptionReference, plan.Handle, cancellationToken);
            if (existing == null)
            {
                throw;
            }

            subscription = existing;
        }

        var mapping = new MaxioSubscriptionMapping
        {
            UserId = user.Id,
            CustomerReference = customerReference,
            MaxioCustomerId = customer.Id,
            ProductHandle = plan.Handle,
            MaxioSubscriptionId = subscription.Id,
            SubscriptionReference = subscriptionReference,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _identityDbContext.MaxioSubscriptionMappings.Add(mapping);
        try
        {
            await _identityDbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var persisted = await _identityDbContext.MaxioSubscriptionMappings
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.UserId == user.Id && item.ProductHandle == plan.Handle, cancellationToken);
            if (persisted == null)
            {
                throw;
            }

            subscription = await _maxio.GetSubscriptionAsync(persisted.MaxioSubscriptionId, cancellationToken);
        }

        return new SubscriptionOperationResult(ToDto(subscription, plan.Handle, plan), mapping.Id != 0);
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(principal);
        if (user == null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var mappings = await _identityDbContext.MaxioSubscriptionMappings
            .AsNoTracking()
            .Where(mapping => mapping.UserId == user.Id)
            .OrderBy(mapping => mapping.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var subscriptions = new List<SubscriptionDto>(mappings.Count);
        foreach (var mapping in mappings)
        {
            var subscription = await _maxio.GetSubscriptionAsync(mapping.MaxioSubscriptionId, cancellationToken);
            subscriptions.Add(ToDto(subscription, mapping.ProductHandle));
        }

        return subscriptions;
    }

    private async Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName) ? null : await _userManager.FindByNameAsync(userName);
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(ApplicationUser user, string reference, CancellationToken cancellationToken)
    {
        var existing = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        var email = user.Email ?? user.UserName;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("The authenticated user does not have an email address for billing.");
        }

        var customer = new MaxioCustomer
        {
            Reference = reference,
            FirstName = email,
            LastName = email,
            Email = email
        };

        try
        {
            return await _maxio.CreateCustomerAsync(customer, StableToken($"customer:{reference}"), cancellationToken);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 409)
        {
            var existingCustomer = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
            if (existingCustomer == null)
            {
                throw;
            }

            return existingCustomer;
        }
        catch (HttpRequestException)
        {
            var recoveredCustomer = await _maxio.GetCustomerByReferenceAsync(reference, cancellationToken);
            if (recoveredCustomer == null)
            {
                throw;
            }

            return recoveredCustomer;
        }
    }

    private async Task<MaxioSubscription?> FindExistingSubscriptionAsync(string customerReference, string subscriptionReference, string planHandle, CancellationToken cancellationToken)
    {
        var subscriptions = await _maxio.ListSubscriptionsAsync(cancellationToken);
        return subscriptions.FirstOrDefault(subscription => string.Equals(subscription.Reference, subscriptionReference, StringComparison.Ordinal))
            ?? subscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.Customer?.Reference, customerReference, StringComparison.Ordinal) &&
                string.Equals(subscription.Product?.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
    }

    private static SubscriptionDto ToDto(MaxioSubscription subscription, string fallbackPlanHandle, MaxioProduct? fallbackPlan = null)
    {
        var plan = subscription.Product;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = plan?.Handle ?? fallbackPlan?.Handle ?? fallbackPlanHandle,
            PlanName = plan?.Name ?? fallbackPlan?.Name ?? fallbackPlan?.Handle ?? fallbackPlanHandle,
            PriceInCents = subscription.ProductPriceInCents != 0 ? subscription.ProductPriceInCents : fallbackPlan?.PriceInCents ?? plan?.PriceInCents ?? 0,
            State = subscription.State ?? string.Empty,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt
        };
    }

    private static string CustomerReferenceFor(string externalIdentity) => $"eshop:user:{StableToken($"identity:{externalIdentity}")}";

    private static string SubscriptionReferenceFor(string externalIdentity, string planHandle) => $"eshop:subscription:{StableToken($"identity:{externalIdentity}")}:{planHandle}";

    private static string StableToken(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
