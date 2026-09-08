using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.SubscriptionServices;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint :
    EndpointBaseAsync.WithRequest<CreateSubscriptionRequest>.WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly ICurrentShopperService _currentShopperService;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService, ICurrentShopperService currentShopperService)
    {
        _subscriptionService = subscriptionService;
        _currentShopperService = currentShopperService;
    }

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Subscribes the current user to a subscription plan",
        Description = "Subscribes the current user to the plan identified by the requested plan handle. Idempotent: subscribing again to a plan the user already holds returns the existing subscription.",
        OperationId = "subscriptions.subscribe",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return BadRequest("A request body with a plan handle is required.");
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest("A subscription plan handle is required.");
        }

        var shopper = await _currentShopperService.GetCurrentShopperAsync(User, cancellationToken);
        if (shopper == null)
        {
            return Unauthorized();
        }

        var result = await _subscriptionService.SubscribeAsync(shopper, request.PlanHandle.Trim(), cancellationToken);
        response.Subscription = result.Subscription;
        response.CreatedNew = result.CreatedNew;

        return response;
    }
}
