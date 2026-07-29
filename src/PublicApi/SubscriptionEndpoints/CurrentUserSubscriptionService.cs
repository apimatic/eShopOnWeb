using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the caller's identity from the JWT and maps it to a stable billing customer
/// reference (the eShop user id), then delegates to the billing service.
/// </summary>
public class CurrentUserSubscriptionService : ISubscriptionAppService
{
    private readonly ISubscriptionBillingService _billing;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentUserSubscriptionService(
        ISubscriptionBillingService billing,
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager)
    {
        _billing = billing;
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken)
        => _billing.GetPlansAsync(cancellationToken);

    public async Task<SubscribeResult> SubscribeAsync(string planHandle, CancellationToken cancellationToken)
    {
        var customer = await ResolveCurrentCustomerAsync();
        return await _billing.SubscribeAsync(customer, planHandle, cancellationToken);
    }

    public async Task<IReadOnlyCollection<CustomerSubscription>> GetMySubscriptionsAsync(CancellationToken cancellationToken)
    {
        var customer = await ResolveCurrentCustomerAsync();
        return await _billing.GetSubscriptionsAsync(customer, cancellationToken);
    }

    /// <summary>
    /// Builds <see cref="BillingCustomerInfo"/> for the authenticated user. The user id is used as
    /// the billing customer reference so the mapping is stable and customer creation is idempotent.
    /// </summary>
    private async Task<BillingCustomerInfo> ResolveCurrentCustomerAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var userName = principal?.FindFirstValue(ClaimTypes.Name) ?? principal?.Identity?.Name;

        if (string.IsNullOrWhiteSpace(userName))
        {
            // [Authorize] should prevent this; guard defensively.
            throw new SubscriptionBillingException("No authenticated user.", SubscriptionBillingError.Validation);
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new SubscriptionBillingException($"User '{userName}' was not found.", SubscriptionBillingError.NotFound);
        }

        var email = user.Email ?? userName;
        var (firstName, lastName) = DeriveName(email);

        return new BillingCustomerInfo(reference: user.Id, email: email, firstName: firstName, lastName: lastName);
    }

    /// <summary>
    /// Derives a first/last name from the email local part. eShop identities carry only an email,
    /// but Maxio requires a name on the customer record; the email remains the meaningful key.
    /// </summary>
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email;
        var atIndex = email.IndexOf('@');
        if (atIndex > 0)
        {
            localPart = email[..atIndex];
        }

        var firstName = string.IsNullOrWhiteSpace(localPart)
            ? "eShop"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(localPart);

        return (firstName, "eShopOnWeb Subscriber");
    }
}
