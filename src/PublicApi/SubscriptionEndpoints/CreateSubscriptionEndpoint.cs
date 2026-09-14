using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionServices;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

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
        Summary = "Subscribes the current user to a plan",
        Description = "Ensures a Maxio customer exists for the current user and subscribes them to the requested plan. Repeats are idempotent.",
        OperationId = "subscriptions.create",
        Tags = new[] { "Subscriptions" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync([FromBody] CreateSubscriptionRequest? request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request?.CorrelationId() ?? System.Guid.NewGuid());

        string? planHandle = request?.PlanHandle?.Trim();
        if (string.IsNullOrEmpty(planHandle))
        {
            return BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "The planHandle is required."
            });
        }

        var user = await ResolveCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new ErrorDetails
            {
                StatusCode = StatusCodes.Status401Unauthorized,
                Message = "The token does not identify an existing user."
            });
        }

        try
        {
            string customerEmail = string.IsNullOrWhiteSpace(user.Email) ? user.UserName ?? string.Empty : user.Email;
            var result = await _subscriptionService.SubscribeAsync(user.UserName!, customerEmail, planHandle, cancellationToken);

            response.Subscription = result.Subscription;
            response.AlreadySubscribed = !result.Created;

            if (result.Created)
            {
                return StatusCode(StatusCodes.Status201Created, response);
            }

            return Ok(response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = ex.Message
            });
        }
    }

    private async Task<ApplicationUser?> ResolveCurrentUserAsync()
    {
        string? userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(userName);
    }
}
