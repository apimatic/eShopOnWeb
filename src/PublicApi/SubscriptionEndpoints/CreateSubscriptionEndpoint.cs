using System;
using System.Linq;
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
/// Enrolls the authenticated user into a Maxio subscription plan (idempotent)
/// </summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Subscribes the current user to a plan",
        Description = "Ensures a Maxio customer exists for the authenticated user and enrolls them into the requested subscription plan. Returns the existing subscription when the user is already subscribed to the plan.",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentUserAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var enrollment = await _subscriptionService.EnrollAsync(
            MaxioUserInfoFactory.FromUser(user),
            request?.ProductHandle,
            cancellationToken);

        return Ok(new CreateSubscriptionResponse(request!.CorrelationId())
        {
            Subscription = SubscriptionDtoMapper.ToDto(enrollment.Subscription, enrollment.Created)
        });
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var username = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }
        return await _userManager.FindByNameAsync(username);
    }
}
