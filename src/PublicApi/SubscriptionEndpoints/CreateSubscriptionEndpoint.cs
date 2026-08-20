using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated shopper in a Maxio subscription plan. Idempotent.
/// </summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateShopperSubscriptionRequest>
    .WithActionResult<CreateShopperSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _billing;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser> userManager)
    {
        _billing = billing;
        _userManager = userManager;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribe to a plan",
        Description = "Ensures a Maxio customer exists for the caller and creates (or returns) their subscription.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })]
    public override async Task<ActionResult<CreateShopperSubscriptionResponse>> HandleAsync(
        [FromBody] CreateShopperSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var shopper = await ShopperIdentityFactory.FromUserAsync(_userManager, User, cancellationToken);
        if (shopper is null)
        {
            return Unauthorized();
        }

        request ??= new CreateShopperSubscriptionRequest();
        var result = await _billing.SubscribeAsync(shopper, request.ProductHandle, cancellationToken);
        var response = new CreateShopperSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ShopperSubscriptionDto.From(result.Subscription),
            Created = result.Created
        };

        if (result.Created)
        {
            return Created($"api/my-subscriptions", response);
        }

        return Ok(response);
    }
}
