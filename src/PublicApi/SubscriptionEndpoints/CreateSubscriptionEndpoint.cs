using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to the requested plan. Ensures a Maxio
/// customer exists for the user (idempotently) and returns the confirmed
/// plan, price, state and next billing date. A repeated request for the same
/// plan returns the existing subscription instead of creating a duplicate.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes to a plan",
        Description = "Subscribes the authenticated user to the plan identified by productHandle",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.ProductHandle))
        {
            return BadRequest(new { Message = "productHandle is required." });
        }

        SubscriptionStatus status;
        try
        {
            status = await _subscriptionService.SubscribeAsync(
                user.Id, user.UserName!, user.Email ?? user.UserName!, request.ProductHandle, cancellationToken);
        }
        catch (SubscriptionPlanNotFoundException)
        {
            return NotFound(new { Message = $"No subscription plan with handle '{request.ProductHandle}' is available." });
        }

        return new CreateSubscriptionResponse
        {
            Subscription = ToDto(status)
        };
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var userName = User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }
        return await _userManager.FindByNameAsync(userName);
    }

    internal static SubscriptionStatusDto ToDto(SubscriptionStatus status) =>
        new SubscriptionStatusDto
        {
            MaxioSubscriptionId = status.MaxioSubscriptionId,
            MaxioCustomerId = status.MaxioCustomerId,
            ProductHandle = status.ProductHandle,
            ProductName = status.ProductName,
            PriceInCents = status.PriceInCents,
            PriceDisplay = $"{status.Currency} {status.PriceInCents / 100.0:0.00}",
            Currency = status.Currency,
            State = status.State,
            NextBillingDateUtc = status.NextBillingDateUtc,
            CreatedAtUtc = status.CreatedAtUtc
        };
}
