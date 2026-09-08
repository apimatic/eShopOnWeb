using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan. Idempotent: enrolling twice in the same plan
/// returns the existing subscription instead of creating a duplicate.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
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
        Summary = "Subscribes to a plan",
        Description = "Enrolls the signed-in shopper in the given subscription plan (idempotent)",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new { Message = "PlanHandle is required." });
        }

        var user = await User.GetApplicationUserAsync(_userManager);
        if (user == null)
        {
            return Unauthorized();
        }

        var nameParts = user.GetNameParts();
        var command = new SubscribeCommand(
            UserReference: user.Id,
            Email: user.Email ?? user.UserName!,
            FirstName: nameParts.FirstName,
            LastName: nameParts.LastName,
            PlanHandle: request.PlanHandle);

        var result = await _subscriptionService.SubscribeAsync(command, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.From(result.Subscription),
            AlreadySubscribed = result.AlreadySubscribed
        };

        return result.AlreadySubscribed ? Ok(response) : Created($"api/my-subscriptions", response);
    }
}
