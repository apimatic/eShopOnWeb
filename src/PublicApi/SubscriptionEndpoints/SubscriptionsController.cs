using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SubscriptionsController : ControllerBase
{
    private readonly SubscriptionEnrollmentService _subscriptions;

    public SubscriptionsController(SubscriptionEnrollmentService subscriptions)
    {
        _subscriptions = subscriptions;
    }

    [HttpGet("subscription-plans")]
    [ProducesResponseType(typeof(SubscriptionPlansResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SubscriptionPlansResponse>> GetPlans(CancellationToken cancellationToken)
    {
        try
        {
            var response = new SubscriptionPlansResponse();
            response.Plans.AddRange(await _subscriptions.GetPlansAsync(cancellationToken));
            return Ok(response);
        }
        catch (MaxioApiException)
        {
            return Problem("The billing service could not process this request.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    [HttpPost("subscriptions")]
    [ProducesResponseType(typeof(SubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SubscriptionDto>> Subscribe(CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            ModelState.AddModelError(nameof(request.ProductHandle), "ProductHandle is required.");
            return ValidationProblem(ModelState);
        }

        var userName = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await _subscriptions.SubscribeAsync(userName, request.ProductHandle, cancellationToken));
        }
        catch (SubscriptionValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (MaxioApiException)
        {
            return Problem("The billing service could not process this request.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    [HttpGet("my-subscriptions")]
    [ProducesResponseType(typeof(MySubscriptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MySubscriptionsResponse>> GetMine(CancellationToken cancellationToken)
    {
        var userName = User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Unauthorized();
        }

        try
        {
            var response = new MySubscriptionsResponse();
            response.Subscriptions.AddRange(await _subscriptions.GetMySubscriptionsAsync(userName, cancellationToken));
            return Ok(response);
        }
        catch (SubscriptionValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (MaxioApiException)
        {
            return Problem("The billing service could not process this request.", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
