using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionsController(
        ISubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpGet("subscription-plans")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionPlan>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionPlan>>> ListPlans(CancellationToken cancellationToken)
    {
        return Ok(await _billingService.ListPlansAsync(cancellationToken));
    }

    [HttpPost("subscriptions")]
    [ProducesResponseType(typeof(SubscriptionDetails), StatusCodes.Status200OK)]
    public async Task<ActionResult<SubscriptionDetails>> Subscribe(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var user = await ResolveBillingUserAsync();
        if (user is null) return Unauthorized();

        return Ok(await _billingService.SubscribeAsync(user, request.ProductHandle, cancellationToken));
    }

    [HttpGet("my-subscriptions")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionDetails>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionDetails>>> ListMine(CancellationToken cancellationToken)
    {
        var user = await ResolveBillingUserAsync();
        if (user is null) return Unauthorized();

        return Ok(await _billingService.ListSubscriptionsAsync(user, cancellationToken));
    }

    private async Task<BillingUser?> ResolveBillingUserAsync()
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName)) return null;

        var applicationUser = await _userManager.FindByNameAsync(userName);
        if (applicationUser is null) return null;

        var email = applicationUser.Email ?? applicationUser.UserName;
        if (string.IsNullOrWhiteSpace(email)) return null;

        var localPart = email.Split('@', 2)[0];
        var nameParts = localPart
            .Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
            .ToArray();
        var firstName = nameParts.FirstOrDefault() ?? "eShop";
        var lastName = nameParts.Length > 1 ? string.Join(' ', nameParts.Skip(1)) : "Customer";

        return new BillingUser(applicationUser.Id, email, firstName, lastName);
    }
}
