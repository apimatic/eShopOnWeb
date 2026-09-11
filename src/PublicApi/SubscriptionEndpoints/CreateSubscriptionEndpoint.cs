using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioService _maxio;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioService maxio, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribe to a plan",
        Description = "Creates a Maxio customer (idempotent) and subscribes to the requested plan.",
        OperationId = "subscription.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        [FromBody] CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "";
        if (string.IsNullOrEmpty(userName))
            return Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
            return NotFound();

        var refValue = user.Email ?? user.UserName ?? user.Id.ToString();

        var sub = await _maxio.CreateSubscriptionAsync(
            request.ProductHandle,
            refValue,
            user.UserName ?? "User",
            user.UserName ?? "Name",
            user.Email ?? refValue);

        response.SubscriptionId = sub.Id;
        response.State = sub.State;
        response.ProductHandle = sub.ProductHandle;
        response.ProductName = sub.ProductName;
        response.NextBillingAt = sub.NextBillingAt;
        response.Price = sub.ProductPriceInCents / 100m;
        return Ok(response);
    }
}
