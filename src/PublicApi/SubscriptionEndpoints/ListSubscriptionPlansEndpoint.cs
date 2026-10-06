using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared error-to-HTTP-status mapping for the subscription endpoints.
/// </summary>
internal static class SubscriptionEndpointErrorMapper
{
    public static ActionResult ToActionResult(this MaxioApiException ex) =>
        new ObjectResult(new
        {
            statusCode = 502,
            message = $"Maxio Advanced Billing rejected the request: {ex.Message}",
            errors = ex.Errors
        })
        {
            StatusCode = 502
        };
}

/// <summary>
/// Lists the subscription plans available for purchase.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListSubscriptionPlansEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListSubscriptionPlansResponse>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    [HttpGet("api/subscription-plans")]
    [SwaggerOperation(
        Summary = "Lists subscription plans",
        Description = "Lists the recurring subscription plans available for purchase",
        OperationId = "subscriptions.listPlans",
        Tags = new[] { "SubscriptionEndpoints" })
    ]
    public override async Task<ActionResult<ListSubscriptionPlansResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var plans = await _subscriptionService.ListPlansAsync(cancellationToken);
        var response = new ListSubscriptionPlansResponse();
        foreach (var plan in plans)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Handle = plan.Handle,
                Name = plan.Name,
                Description = plan.Description,
                PriceInCents = plan.PriceInCents,
                Price = plan.Price,
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit,
                TrialDays = plan.TrialDays,
                RequiresPaymentMethod = plan.RequiresPaymentMethod,
                IsActive = plan.IsActive
            });
        }
        return Ok(response);
    }
}