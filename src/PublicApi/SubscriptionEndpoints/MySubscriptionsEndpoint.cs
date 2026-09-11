using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[Authorize]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly IMaxioService _maxio;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(IMaxioService maxio, UserManager<ApplicationUser> userManager)
    {
        _maxio = maxio;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(Summary = "Get current user's subscriptions", Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var user = await _userManager.GetUserAsync(User);
        var email = user?.Email ?? User.Identity?.Name ?? "anonymous";
        var subs = await _maxio.GetMySubscriptionsAsync(email, cancellationToken);
        return new MySubscriptionsResponse
        {
            Subscriptions = subs.Select(s => new SubscribeResponse
            {
                SubscriptionId = s.Id,
                ProductHandle = s.ProductHandle,
                State = s.State,
                NextBilling = s.NextBilling,
                CustomerReference = s.CustomerReference
            }).ToList()
        };
    }
}
