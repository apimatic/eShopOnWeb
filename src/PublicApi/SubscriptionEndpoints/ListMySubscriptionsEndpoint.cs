using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListMySubscriptionsResponse>
{
    private readonly ISubscriptionBillingService _billing;
    private readonly AuthenticatedBillingUserResolver _users;

    public ListMySubscriptionsEndpoint(
        ISubscriptionBillingService billing,
        AuthenticatedBillingUserResolver users)
    {
        _billing = billing;
        _users = users;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the signed-in user's subscriptions from Maxio",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<ListMySubscriptionsResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var user = await _users.ResolveAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var subscriptions = await _billing.GetSubscriptionsAsync(user, cancellationToken);
        return Ok(new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(SubscriptionDto.From).ToList()
        });
    }
}
