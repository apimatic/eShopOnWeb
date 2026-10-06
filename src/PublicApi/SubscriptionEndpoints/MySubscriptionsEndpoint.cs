using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Services.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscriptions of the signed-in user.
/// </summary>
[Authorize]
public class MySubscriptionsEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<MySubscriptionsResponse>
{
    private readonly IMaxioClient _maxioClient;

    public MySubscriptionsEndpoint(IMaxioClient maxioClient)
    {
        _maxioClient = maxioClient;
    }

    [HttpGet("api/my-subscriptions")]
    [SwaggerOperation(
        Summary = "Lists the current user's subscriptions",
        Description = "Lists the subscriptions belonging to the signed-in user",
        OperationId = "subscriptions.mine",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<MySubscriptionsResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var response = new MySubscriptionsResponse();

        string userName = User.Identity?.Name ?? "";
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized();
        }

        var customer = await _maxioClient.FindCustomerByReferenceAsync(userName, cancellationToken);
        if (customer == null)
        {
            return Ok(response);
        }

        var subscriptions = await _maxioClient.GetCustomerSubscriptionsAsync(customer.Id, cancellationToken);
        foreach (var subscription in subscriptions)
        {
            response.Subscriptions.Add(SubscriptionMapper.ToSubscriptionDto(subscription));
        }

        return Ok(response);
    }
}
