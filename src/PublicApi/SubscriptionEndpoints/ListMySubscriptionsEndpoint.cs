using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the caller's subscriptions
/// </summary>
public class ListMySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public ListMySubscriptionsEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Lists the caller's subscriptions",
        Description = "Lists the subscriptions of the Maxio customer that represents the caller",
        OperationId = "subscriptions.listMine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var customer = MaxioCustomer.FromPrincipal(User);
        if (customer is null)
        {
            return Unauthorized();
        }

        var response = new MySubscriptionsResponse();
        var subscriptions = await _subscriptionService.ListSubscriptionsAsync(customer, cancellationToken);
        response.Subscriptions.AddRange(subscriptions);
        return Ok(response);
    }
}
