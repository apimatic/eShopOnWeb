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
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<MySubscriptionsRequest>
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
    [SwaggerOperation(
        Summary = "My subscriptions",
        Description = "Lists the current user's Maxio subscriptions.",
        OperationId = "subscription.my",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(
        [FromQuery] MySubscriptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? User.Identity?.Name ?? "";
        if (string.IsNullOrEmpty(userName))
            return Unauthorized();

        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
            return NotFound();

        var refValue = user.Email ?? user.UserName ?? user.Id.ToString();
        var subs = await _maxio.GetMySubscriptionsAsync(refValue);
        response.Subscriptions = subs.Select(s => new MySubscriptionResponse
        {
            Id = s.Id,
            State = s.State,
            ProductHandle = s.ProductHandle,
            ProductName = s.ProductName,
            NextBillingAt = s.NextBillingAt,
            Price = s.ProductPriceInCents / 100m
        }).ToList();
        return response;
    }
}
