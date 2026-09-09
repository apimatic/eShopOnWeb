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
/// Subscribes the authenticated user to a plan. Idempotent: repeated calls for
/// a plan the user already holds return the existing subscription instead of
/// creating a second one.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionManager _subscriptionManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(ISubscriptionManager subscriptionManager,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionManager = subscriptionManager;
        _userManager = userManager;
    }

    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the current user to a plan",
        Description = "Ensures a Maxio customer exists for the user and creates (or returns the existing) subscription",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        [FromBody] CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request?.ProductHandle))
        {
            return BadRequest(new { message = "A productHandle is required." });
        }

        var user = await _userManager.FindByNameAsync(User.Identity?.Name ?? string.Empty);
        if (user is null)
        {
            return Unauthorized();
        }

        SubscriptionResult result;
        try
        {
            result = await _subscriptionManager.SubscribeAsync(user.Id, user.Email!, request.ProductHandle,
                cancellationToken);
        }
        catch (MaxioNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (MaxioConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (MaxioApiException ex) when (ex.StatusCode == 422)
        {
            return Conflict(new { message = "Maxio rejected the subscription.", errors = ex.Errors });
        }
        catch (MaxioException ex)
        {
            return StatusCode(503, new { message = $"The billing system is currently unavailable: {ex.Message}" });
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ToDto(result)
        };

        return result.Created
            ? Created($"api/my-subscriptions/{result.MaxioSubscriptionId}", response)
            : Ok(response);
    }

    internal static SubscriptionDto ToDto(SubscriptionResult result) => new()
    {
        Id = result.MaxioSubscriptionId,
        State = result.State,
        PlanHandle = result.PlanHandle,
        PlanName = result.PlanName,
        PriceInCents = result.PriceInCents,
        Price = result.PriceInCents / 100m,
        Currency = result.Currency,
        NextBillingDate = result.NextBillingDate,
        ActivatedAt = result.ActivatedAt
    };
}
