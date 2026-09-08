using System.Linq;
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
/// Lists the Maxio subscriptions of the authenticated shopper.
/// </summary>
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly MaxioSubscriptionService _maxio;

    public MySubscriptionsEndpoint(MaxioSubscriptionService maxio)
    {
        _maxio = maxio;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current shopper's subscriptions",
        Description = "Lists the subscriptions of the Maxio customer that represents the authenticated shopper",
        OperationId = "subscriptions.my",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var email = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(email))
        {
            return Unauthorized();
        }

        var subscriptions = await _maxio.ListMySubscriptionsAsync(email, cancellationToken);

        var response = new MySubscriptionsResponse
        {
            Subscriptions = subscriptions.ToList()
        };
        return Ok(response);
    }
}
