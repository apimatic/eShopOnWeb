using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions on the Maxio site.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly IMaxioBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public MySubscriptionsEndpoint(IMaxioBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current shopper's subscriptions",
        Description = "Lists the subscriptions belonging to the authenticated shopper on the configured Maxio site.",
        OperationId = "subscriptions.mine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsResponse();

        var user = await ResolveCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var customer = await _billingService.FindCustomerAsync(user.Id, cancellationToken);
        if (customer is null)
        {
            return response;
        }

        var subscriptions = await _billingService.ListUserSubscriptionsAsync(customer, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(SubscriptionMappings.ToDto(subscription));
        }

        return response;
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
}
