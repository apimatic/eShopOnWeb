using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class SubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeRequest>
    .WithActionResult<SubscribeResponse>
{
    private readonly IMaxioService _maxio;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionsEndpoint(IMaxioService maxio, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(Summary = "Subscribe to a plan", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<SubscribeResponse>> HandleAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.GetUserAsync(User);
        var email = user?.Email ?? User.Identity?.Name ?? "anonymous";
        var result = await _maxio.SubscribeAsync(email, request.ProductHandle, cancellationToken);
        return new SubscribeResponse
        {
            SubscriptionId = result.Id,
            ProductHandle = result.ProductHandle,
            State = result.State,
            NextBilling = result.NextBilling,
            CustomerReference = result.CustomerReference
        };
    }
}
