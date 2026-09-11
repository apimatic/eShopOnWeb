using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class PostSubscriptionEndpoint : IEndpoint
{
    private readonly ISubscriptionPlanService _subscriptionPlanService;

    public PostSubscriptionEndpoint(ISubscriptionPlanService subscriptionPlanService)
    {
        _subscriptionPlanService = subscriptionPlanService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpRequest request, PostSubscriptionRequest subscribeRequest) =>
            {
                var userId = request.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var result = await _subscriptionPlanService.SubscribeUserAsync(userId, subscribeRequest.ProductHandle);

                var response = new PostSubscriptionResponse()
                {
                    SubscriptionId = result.SubscriptionId,
                    State = result.State,
                    CreatedAt = result.CreatedAt,
                    NextAssessmentAt = result.NextAssessmentAt,
                    ProductName = result.ProductName
                };

                return Results.Ok(response);
            })
            .Produces<PostSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
