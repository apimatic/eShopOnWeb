using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions belonging to the signed-in shopper. Subscriptions are the system of
/// record in Maxio Advanced Billing; this endpoint reads them live so state is always current.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(
        ISubscriptionBillingService subscriptionBillingService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Returns every Maxio subscription currently owned by the signed-in shopper.",
        OperationId = "subscriptions.mine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsResponse();

        var applicationUser = await CurrentUserResolver.ResolveAsync(_userManager, User, cancellationToken);
        if (applicationUser is null || string.IsNullOrWhiteSpace(applicationUser.Email))
        {
            return Unauthorized();
        }

        var subscriptions = await _subscriptionBillingService.ListMySubscriptionsAsync(
            applicationUser.Id,
            applicationUser.Email,
            cancellationToken);

        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(SubscriptionDto.FromMaxio(subscription));
        }

        return Ok(response);
    }
}
