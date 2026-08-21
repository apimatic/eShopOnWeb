using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService subscriptionBillingService)
    {
        _subscriptionBillingService = subscriptionBillingService;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current shopper to a plan",
        Description = "Idempotently ensures a Maxio customer and subscription for the authenticated shopper.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            ModelState.AddModelError(nameof(request.ProductHandle), "A product handle is required.");
            return ValidationProblem(ModelState);
        }

        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }

        try
        {
            var response = await _subscriptionBillingService.SubscribeAsync(
                userName,
                request.ProductHandle,
                cancellationToken);

            return response.AlreadyExisted
                ? Ok(response)
                : StatusCode(StatusCodes.Status201Created, response);
        }
        catch (SubscriptionBillingException exception)
        {
            return SubscriptionEndpointHelpers.FromException(this, exception);
        }
    }
}
