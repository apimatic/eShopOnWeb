using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: subscribing to a plan the
/// shopper is already on returns the existing subscription instead of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        ILogger<CreateSubscriptionEndpoint> logger)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
        _logger = logger;
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("api/subscriptions")]
    [SwaggerOperation(
        Summary = "Subscribes the authenticated shopper to a plan",
        Description = "Creates (or returns, when already present) the shopper's subscription to the requested plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CreateSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var correlationId = request?.CorrelationId() ?? Guid.NewGuid();

        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new SubscriptionErrorPayload("A 'planHandle' identifying the plan to subscribe to is required."));
        }

        var user = await SubscriptionUserResolver.GetAuthenticatedUserAsync(User, _userManager);
        if (user is null)
        {
            _logger.LogWarning("Could not resolve the authenticated token subject to an eShop user.");
            return Unauthorized();
        }

        var contact = new SubscribeContact
        {
            FirstName = request.FirstName,
            LastName = request.LastName
        };

        try
        {
            var result = await _subscriptionService.SubscribeAsync(user,
                request.PlanHandle,
                contact,
                request.IdempotencyKey,
                cancellationToken);

            var response = new CreateSubscriptionResponse(correlationId)
            {
                Subscription = result.Subscription
            };

            return result.Created
                ? StatusCode(StatusCodes.Status201Created, response)
                : Ok(response);
        }
        catch (Exception ex) when (ex is SubscriptionPlanNotFoundException
                                   or MaxioConfigurationException
                                   or MaxioApiException)
        {
            return SubscriptionEndpointErrorMapper.ToObjectResult(ex, _logger);
        }
    }
}
