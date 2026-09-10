using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions, as reflected in Maxio.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _billingService;

    public ListMySubscriptionsEndpoint(ISubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Lists the subscriptions of the authenticated user's Maxio customer",
        OperationId = "subscriptions.mine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var username = User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Unauthorized();
        }

        try
        {
            var subscriptions = await _billingService.GetSubscriptionsAsync(username, cancellationToken);
            var response = new ListMySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(MySubscriptionModel.From).ToList()
            };
            return Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionErrorMapper.ToActionResult(ex);
        }
    }
}
