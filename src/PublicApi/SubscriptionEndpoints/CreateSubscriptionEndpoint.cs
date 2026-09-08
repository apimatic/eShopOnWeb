using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: a shopper can never end up
/// with two subscriptions to the same plan.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current shopper to a plan",
        Description = "Ensures a Maxio customer exists for the authenticated shopper and subscribes them to the plan with the given handle.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var user = await ResolveCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new { message = "planHandle is required." });
        }

        var (firstName, lastName) = SplitDisplayName(user.Email ?? user.UserName ?? string.Empty);

        var customer = await _billingService.EnsureCustomerAsync(
            user.Id,
            user.Email ?? user.UserName ?? string.Empty,
            firstName,
            lastName,
            cancellationToken);

        var result = await _billingService.SubscribeToPlanAsync(customer, request.PlanHandle.Trim(), cancellationToken);

        response.Subscription = SubscriptionMappings.ToDto(result.Subscription);
        response.WasCreated = result.WasCreated;

        if (result.WasCreated)
        {
            return StatusCode(201, response);
        }

        return Ok(response);
    }

    private async Task<ApplicationUser?> ResolveCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(userName);
    }

    private static (string FirstName, string LastName) SplitDisplayName(string emailOrName)
    {
        if (string.IsNullOrWhiteSpace(emailOrName))
        {
            return ("eShop", "Shopper");
        }

        var localPart = emailOrName.Contains('@', StringComparison.Ordinal)
            ? emailOrName.Split('@')[0]
            : emailOrName;

        var tokens = localPart.Split(new[] { '.', '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return (localPart, "Shopper");
        }

        if (tokens.Length == 1)
        {
            return (tokens[0], "Shopper");
        }

        return (tokens[0], string.Join(" ", tokens.Skip(1)));
    }
}
