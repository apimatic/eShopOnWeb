using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioSubscriptionService subscriptionService, HttpContext httpContext) =>
            {
                return await HandleRequestAsync(subscriptionService, httpContext);
            })
            .RequireAuthorization()
            .Produces<SubscriptionPlansListResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError)
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleRequestAsync(
        MaxioSubscriptionService subscriptionService,
        HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var plans = await subscriptionService.GetPlansAsync();
            var response = new SubscriptionPlansListResponse { Plans = plans };
            return Results.Ok(response);
        }
        catch (MaxioException)
        {
            return Results.StatusCode(500);
        }
    }
}

public class SubscriptionPlansListResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
