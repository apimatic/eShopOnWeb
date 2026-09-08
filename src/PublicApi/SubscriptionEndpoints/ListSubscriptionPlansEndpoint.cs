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
/// Lists the subscription plans the signed-in shopper can subscribe to.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public ListSubscriptionPlansEndpoint(
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService subscriptionBillingService)
    {
        _userManager = userManager;
        _subscriptionBillingService = subscriptionBillingService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists the available subscription plans",
        Description = "Lists the subscription plans the signed-in user may subscribe to",
        OperationId = "subscriptions.list-plans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var user = await CurrentUserResolver.GetCurrentUserAsync(User, _userManager);
        if (user is null)
        {
            return Unauthorized();
        }

        var plans = await _subscriptionBillingService.ListPlansAsync(cancellationToken);

        var response = new ListSubscriptionPlansResponse();
        foreach (var plan in plans)
        {
            response.SubscriptionPlans.Add(SubscriptionMapper.ToDto(plan));
        }

        return Ok(response);
    }
}
