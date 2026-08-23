using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly IRecurringSubscriptionService _subscriptions;
    private readonly IAuthenticatedBillingUserProvider _userProvider;

    public MySubscriptionsEndpoint(
        IRecurringSubscriptionService subscriptions,
        IAuthenticatedBillingUserProvider userProvider)
    {
        _subscriptions = subscriptions;
        _userProvider = userProvider;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current shopper's recurring subscriptions",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var user = await _userProvider.GetAsync(User, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        try
        {
            var subscriptions = await _subscriptions.ListForUserAsync(user.Id, cancellationToken);
            return Ok(new MySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(SubscriptionDto.From).ToArray()
            });
        }
        catch (BillingProviderException exception)
        {
            return SubscriptionEndpointErrors.ToActionResult<MySubscriptionsResponse>(this, exception);
        }
    }
}
