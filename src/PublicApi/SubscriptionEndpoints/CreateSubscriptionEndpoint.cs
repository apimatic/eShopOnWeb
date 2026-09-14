using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in user to a plan
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<SubscribeToPlanRequest>
    .WithActionResult<SubscribeToPlanResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribe the current user to a plan",
        Description = "Enrolls the signed-in user in the plan identified by planHandle. Repeated requests for the same user and plan are idempotent.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<SubscribeToPlanResponse>> HandleAsync(SubscribeToPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new SubscriptionEnrollmentException("A planHandle is required.");
        }

        var user = await this.ResolveAuthenticatedUserAsync(_userManager, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var response = new SubscribeToPlanResponse(request.CorrelationId());

        var subscriber = new SubscriptionSubscriber(user.Id, user.Email ?? user.UserName ?? string.Empty);
        var result = await _subscriptionService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

        response.Subscription = SubscriptionMappings.ToDto(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;

        return response;
    }
}
