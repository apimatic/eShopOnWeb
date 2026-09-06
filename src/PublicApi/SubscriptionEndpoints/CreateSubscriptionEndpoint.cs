using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a subscription for the authenticated user
/// </summary>
public class CreateSubscriptionEndpoint : EndpointBaseAsync
    .WithRequest<CreateSubscriptionRequest>
    .WithActionResult<CreateSubscriptionResponse>
{
    private readonly IMaxioClient _maxioClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly MaxioSettings _maxioSettings;

    public CreateSubscriptionEndpoint(IMaxioClient maxioClient, IHttpContextAccessor httpContextAccessor, MaxioSettings maxioSettings)
    {
        _maxioClient = maxioClient;
        _httpContextAccessor = httpContextAccessor;
        _maxioSettings = maxioSettings;
    }

    [HttpPost("api/subscriptions")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [SwaggerOperation(
        Summary = "Create a new subscription",
        Description = "Subscribe the authenticated user to a plan",
        OperationId = "subscriptions.create",
        Tags = new[] { "SubscriptionEndpoints" }
    )]
    public override async Task<ActionResult<CreateSubscriptionResponse>> HandleAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

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

            var firstNameClaim = user.FindFirst(ClaimTypes.GivenName)?.Value
                ?? user.FindFirst("given_name")?.Value
                ?? "User";

            var lastNameClaim = user.FindFirst(ClaimTypes.Surname)?.Value
                ?? user.FindFirst("family_name")?.Value
                ?? "Account";

            if (string.IsNullOrEmpty(emailClaim))
            {
                return BadRequest(new { error = "User email not found in token" });
            }

            // Create or get existing Maxio customer (idempotent)
            var customer = await _maxioClient.CreateOrGetCustomerAsync(emailClaim, firstNameClaim, lastNameClaim);
            if (customer == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to create or retrieve customer" });
            }

            // Create subscription
            var subscription = await _maxioClient.CreateSubscriptionAsync(customer.Id, request.PlanHandle);

            // Get product details for pricing info
            var product = await _maxioClient.GetProductByHandleAsync(request.PlanHandle);

            response.SubscriptionId = subscription.Id;
            response.CustomerId = customer.Id;
            response.State = subscription.State;
            response.ActivatedAt = subscription.ActivatedAt;
            response.NextBillingDate = subscription.NextAssessmentAt;
            if (product != null)
            {
                response.MonthlyPrice = product.PriceInCents / 100m;
            }

            return response;
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
