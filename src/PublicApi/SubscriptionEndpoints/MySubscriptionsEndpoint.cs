using System;
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
/// Lists the signed-in shopper's subscriptions on Maxio Advanced Billing.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioSubscriptionService _subscriptionService;

    public MySubscriptionsEndpoint(
        UserManager<ApplicationUser> userManager,
        MaxioSubscriptionService subscriptionService)
    {
        _userManager = userManager;
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current shopper's subscriptions",
        Description = "Lists the subscriptions the signed-in shopper holds on Maxio Advanced Billing, including plan, price, state and next billing date.",
        OperationId = "subscriptions.mySubscriptions",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsResponse(Guid.NewGuid());

        var shopper = await CurrentShopperAsync(cancellationToken);
        if (shopper is null || string.IsNullOrWhiteSpace(shopper.Email))
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionService.ListSubscriptionsAsync(shopper.Email, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(SubscriptionMapping.ToSubscriptionDto(subscription));
        }

        return response;
    }

    private async Task<ApplicationUser?> CurrentShopperAsync(CancellationToken cancellationToken = default)
    {
        var name = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(name);
    }
}
