using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: a repeated request
/// (e.g. a double-click) returns the existing subscription instead of creating a second one.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
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
    [SwaggerOperation(
        Summary = "Subscribes to a plan",
        Description = "Enrolls the authenticated user into the given subscription plan via Maxio Advanced Billing",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return BadRequest(new { statusCode = 400, message = "productHandle is required." });
        }

        var userName = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Unauthorized();
        }

        SubscriptionResult result;
        try
        {
            result = await _subscriptionService.SubscribeAsync(userName, request.ProductHandle, cancellationToken);
        }
        catch (MaxioPlanNotFoundException ex)
        {
            return NotFound(new { statusCode = 404, message = ex.Message });
        }
        catch (MaxioApiException ex)
        {
            return ex.ToActionResult();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ToDto(result),
            WasExisting = result.WasExisting
        };
        return Ok(response);
    }

    internal static SubscriptionDto ToDto(SubscriptionResult result) => new()
    {
        MaxioSubscriptionId = result.MaxioSubscriptionId,
        MaxioCustomerId = result.MaxioCustomerId,
        ProductHandle = result.ProductHandle,
        ProductName = result.ProductName,
        PriceInCents = result.PriceInCents,
        State = result.State,
        NextBillingAt = result.NextBillingAt,
        CurrentPeriodEndsAt = result.CurrentPeriodEndsAt,
        CreatedAt = result.CreatedAt
    };
}