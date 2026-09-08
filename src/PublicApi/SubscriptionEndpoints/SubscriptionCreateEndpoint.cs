using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Maxio.Contracts;
using Microsoft.eShopWeb.Maxio.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a subscription plan.
/// Idempotent: re-subscribing to a plan the shopper already has returns the existing subscription.
/// </summary>
public class SubscriptionCreateEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionCreateEndpoint(IMaxioBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated shopper to a plan",
        Description = "Ensures a Maxio customer exists for the shopper and creates a subscription to the given plan. " +
                      "Returns the existing subscription (HTTP 200) when the shopper is already subscribed to the plan.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest("A planHandle is required.");
        }

        var userName = User.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Unauthorized();
        }

        var command = new SubscribeCommand
        {
            CustomerReference = user.Id,
            Email = user.Email ?? userName,
            FirstName = request.FirstName,
            LastName = request.LastName,
            ProductHandle = request.PlanHandle
        };

        var result = await _billingService.SubscribeAsync(command, cancellationToken);

        response.Subscription = SubscriptionDtoMapper.ToDto(result.Subscription);
        response.Created = result.Created;

        return result.Created
            ? StatusCode(StatusCodes.Status201Created, response)
            : response;
    }
}
