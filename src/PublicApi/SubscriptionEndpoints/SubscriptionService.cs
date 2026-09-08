using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionService : ISubscriptionService
{
    private const string CustomerReferencePrefix = "eshop-";
    private const string CustomerOrganization = "eShopOnWeb";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SubscribeLocks = new(StringComparer.Ordinal);

    private readonly IMaxioApiClient _api;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioApiClient api, UserManager<ApplicationUser> userManager, ILogger<SubscriptionService> logger)
    {
        _api = api;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var products = await _api.ListPlansAsync(cancellationToken);
        return products.Select(ToPlanDto).OrderBy(plan => plan.Name, StringComparer.Ordinal).ToList();
    }

    public async Task<SubscriptionEnrollment> SubscribeAsync(ClaimsPrincipal principal, string planHandle, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var products = await _api.ListPlansAsync(cancellationToken);
        var plan = products.FirstOrDefault(product => string.Equals(product.Handle, planHandle, StringComparison.OrdinalIgnoreCase));
        if (plan is null)
        {
            throw new InvalidSubscriptionRequestException($"No subscription plan is available with handle '{planHandle}'.");
        }

        var reference = BuildCustomerReference(user.Id);
        var gate = SubscribeLocks.GetOrAdd(reference, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var customer = await _api.FindCustomerByReferenceAsync(reference, cancellationToken);
            if (customer is null)
            {
                customer = await _api.CreateCustomerAsync(BuildCustomer(user, reference), cancellationToken);
                _logger.LogInformation("Created Maxio customer {CustomerId} for eShop user {UserId}", customer.Id, user.Id);
            }

            var subscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
            var existing = subscriptions.FirstOrDefault(subscription =>
                IsEnrolled(subscription) &&
                string.Equals(subscription.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                return new SubscriptionEnrollment(ToSubscriptionDto(existing), created: false);
            }

            var created = await _api.CreateSubscriptionAsync(new MaxioSubscriptionInput
            {
                CustomerReference = reference,
                ProductHandle = plan.Handle,
                PaymentCollectionMethod = MaxioApiClient.RemittanceCollectionMethod
            }, cancellationToken);

            _logger.LogInformation("Created Maxio subscription {SubscriptionId} for customer {CustomerId} on product {ProductHandle}",
                created.Id, customer.Id, plan.Handle);

            return new SubscriptionEnrollment(ToSubscriptionDto(created), created: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(principal);
        var reference = BuildCustomerReference(user.Id);
        var customer = await _api.FindCustomerByReferenceAsync(reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionDto>();
        }

        var subscriptions = await _api.ListCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        return subscriptions
            .Select(ToSubscriptionDto)
            .OrderByDescending(subscription => subscription.StartedAt)
            .ToList();
    }

    private async Task<ApplicationUser> ResolveUserAsync(ClaimsPrincipal principal)
    {
        var userName = EndpointUser.GetUserName(principal);
        var user = userName is null ? null : await _userManager.FindByNameAsync(userName);
        return user ?? throw new SubscriptionAccessDeniedException();
    }

    private static string BuildCustomerReference(string userId)
    {
        return CustomerReferencePrefix + userId.Replace("-", string.Empty);
    }

    private static MaxioCustomerInput BuildCustomer(ApplicationUser user, string reference)
    {
        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName : user.Email;
        var (firstName, lastName) = SplitName(email);
        return new MaxioCustomerInput
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Organization = CustomerOrganization,
            Reference = reference
        };
    }

    private static (string FirstName, string LastName) SplitName(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return ("eShop", "Customer");
        }

        var at = email.IndexOf('@');
        var local = at >= 0 ? email[..at] : email;
        var tokens = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return ("eShop", "Customer");
        }

        if (tokens.Length == 1)
        {
            return (tokens[0], "Customer");
        }

        return (tokens[0], string.Join(' ', tokens.Skip(1)));
    }

    private static bool IsEnrolled(MaxioSubscription subscription)
    {
        return subscription.State is not null &&
            subscription.State is not ("canceled" or "expired" or "failed_to_create");
    }

    private static SubscriptionPlanDto ToPlanDto(MaxioProduct product)
    {
        return new SubscriptionPlanDto
        {
            Id = product.Id,
            Handle = product.Handle,
            Name = product.Name,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit,
            RequiresCreditCard = product.RequireCreditCard,
            Taxable = product.Taxable
        };
    }

    private static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.Product?.Handle,
            PlanName = subscription.Product?.Name,
            PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents,
            Currency = subscription.Currency,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            StartedAt = subscription.CurrentPeriodStartedAt,
            NextBillingAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt
        };
    }
}
