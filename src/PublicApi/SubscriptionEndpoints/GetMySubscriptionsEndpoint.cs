using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Get subscriptions for the authenticated user
/// </summary>
public class GetMySubscriptionsEndpoint : EndpointBaseAsync
    .WithRequest<GetMySubscriptionsRequest>
    .WithActionResult<GetMySubscriptionsResponse>
{
    private readonly IMaxioClient _maxioClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMySubscriptionsEndpoint(IMaxioClient maxioClient, IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet("api/my-subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Get user's subscriptions",
        Description = "Returns all subscriptions for the authenticated user",
        OperationId = "subscriptions.getmine",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<GetMySubscriptionsResponse>> HandleAsync(
        GetMySubscriptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new GetMySubscriptionsResponse(request.CorrelationId());

        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null || !user.Identity?.IsAuthenticated == true)
            {
                return Unauthorized();
            }

            var emailClaim = user.FindFirst(ClaimTypes.Email)?.Value
                ?? user.FindFirst("email")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(emailClaim))
            {
                return BadRequest(new { error = "User email not found in token" });
            }

            // Lookup existing customer (without creating a new one)
            var customer = await _maxioClient.LookupCustomerByEmailAsync(emailClaim);
            if (customer == null)
            {
                // No customer found for this user - they haven't subscribed yet
                return response;
            }

            // Get subscriptions for this customer
            var subscriptions = await _maxioClient.GetCustomerSubscriptionsAsync(customer.Id);

            response.Subscriptions = subscriptions
                .Select(s => new UserSubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    ActivatedAt = s.ActivatedAt,
                    NextBillingDate = s.NextAssessmentAt
                })
                .ToList();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }
}
