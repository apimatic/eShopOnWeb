using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _billing;
    private readonly AuthenticatedBillingUserResolver _users;

    public CreateSubscriptionEndpoint(
        ISubscriptionBillingService billing,
        AuthenticatedBillingUserResolver users)
    {
        _billing = billing;
        _users = users;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Creates an idempotent subscription in Maxio for the signed-in user",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.ResolveAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await _billing.SubscribeAsync(user, request.ProductHandle, cancellationToken);
        var response = new CreateSubscriptionResponse
        {
            Created = result.Created,
            Subscription = SubscriptionDto.From(result.Subscription)
        };

        return result.Created
            ? StatusCode(201, response)
            : Ok(response);
    }
}
