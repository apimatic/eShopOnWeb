using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api")]
public sealed class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _subscriptions;
    private readonly ILogger<SubscriptionController> _logger;

    public SubscriptionController(ISubscriptionService subscriptions, ILogger<SubscriptionController> logger)
    {
        _subscriptions = subscriptions;
        _logger = logger;
    }

    [HttpGet("subscription-plans")]
    public async Task<ActionResult<SubscriptionPlansResponse>> GetPlans(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(new SubscriptionPlansResponse
            {
                SubscriptionPlans = await _subscriptions.GetPlansAsync(cancellationToken)
            });
        }
        catch (MaxioConfigurationException exception)
        {
            _logger.LogError(exception, "Maxio billing is not configured.");
            return Problem("Subscription billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException exception)
        {
            _logger.LogError(exception, "Maxio rejected a plan lookup with status {StatusCode}.", exception.StatusCode);
            return Problem("The subscription provider could not complete the request.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    [HttpPost("subscriptions")]
    public async Task<ActionResult<SubscriptionResponse>> Subscribe(
        [FromBody] SubscribeRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return BadRequest(new { error = "planHandle is required." });
        }

        try
        {
            var subscription = await _subscriptions.SubscribeAsync(User, request.PlanHandle, cancellationToken);
            return Ok(new SubscriptionResponse { Subscription = subscription });
        }
        catch (SubscriptionPlanNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (MaxioConfigurationException exception)
        {
            _logger.LogError(exception, "Maxio billing is not configured.");
            return Problem("Subscription billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException exception)
        {
            _logger.LogError(exception, "Maxio rejected a subscription request with status {StatusCode}.", exception.StatusCode);
            return Problem("The subscription provider could not complete the request.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    [HttpGet("my-subscriptions")]
    public async Task<ActionResult<MySubscriptionsResponse>> GetMySubscriptions(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(new MySubscriptionsResponse
            {
                Subscriptions = await _subscriptions.GetMySubscriptionsAsync(User, cancellationToken)
            });
        }
        catch (MaxioConfigurationException exception)
        {
            _logger.LogError(exception, "Maxio billing is not configured.");
            return Problem("Subscription billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (MaxioApiException exception)
        {
            _logger.LogError(exception, "Maxio rejected a subscription lookup with status {StatusCode}.", exception.StatusCode);
            return Problem("The subscription provider could not complete the request.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public sealed class SubscribeRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> SubscriptionPlans { get; set; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class SubscriptionResponse
{
    public SubscriptionDto? Subscription { get; set; }
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; set; } = Array.Empty<SubscriptionDto>();
}
