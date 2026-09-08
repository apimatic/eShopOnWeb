using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions owned by the signed-in shopper.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public MySubscriptionsEndpoint(
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService subscriptionBillingService)
    {
        _userManager = userManager;
        _subscriptionBillingService = subscriptionBillingService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the signed-in user's subscriptions",
        Description = "Returns every Maxio subscription belonging to the signed-in user",
        OperationId = "subscriptions.my-subscriptions",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var user = await CurrentUserResolver.GetCurrentUserAsync(User, _userManager);
        if (user is null)
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionBillingService.ListSubscriptionsAsync(user.Id, cancellationToken);

        var response = new MySubscriptionsResponse();
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(SubscriptionMapper.ToDto(subscription));
        }

        return Ok(response);
    }
}
