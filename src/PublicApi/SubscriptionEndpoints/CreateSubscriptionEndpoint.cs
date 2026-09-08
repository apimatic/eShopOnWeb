using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan. Ensures a Maxio customer exists for the user
/// (idempotently, so a double-click never creates two customers/subscriptions), then enrolls
/// them and returns the confirmed plan, price, state and next billing date.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        ISubscriptionBillingService subscriptionBillingService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current user to a plan",
        Description = "Subscribes the signed-in shopper to the plan identified by its API handle.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var applicationUser = await CurrentUserResolver.ResolveAsync(_userManager, User, cancellationToken);
        if (applicationUser is null || string.IsNullOrWhiteSpace(applicationUser.Email))
        {
            return Unauthorized();
        }

        var result = await _subscriptionBillingService.SubscribeAsync(
            applicationUser.Id,
            applicationUser.Email,
            request.ProductHandle,
            request.FirstName,
            request.LastName,
            cancellationToken);

        response.Subscription = SubscriptionDto.FromMaxio(result.Subscription);
        response.WasCreated = result.WasCreated;

        return Ok(response);
    }
}
