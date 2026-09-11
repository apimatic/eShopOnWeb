using System;
using System.Collections.Generic;
using System.Linq;
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

public class GetMySubscriptionsEndpoint : IEndpoint
{
    private readonly ISubscriptionPlanService _subscriptionPlanService;

    public GetMySubscriptionsEndpoint(ISubscriptionPlanService subscriptionPlanService)
    {
        _subscriptionPlanService = subscriptionPlanService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpRequest request) =>
            {
                var userId = request.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Results.Unauthorized();
                }

                var subscriptions = await _subscriptionPlanService.GetUserSubscriptionsAsync(userId);

                var response = new GetMySubscriptionsResponse()
                {
                    Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
                    {
                        SubscriptionId = s.SubscriptionId,
                        State = s.State,
                        ProductName = s.ProductName,
                        Price = s.Price,
                        CreatedAt = s.CreatedAt,
                        NextAssessmentAt = s.NextAssessmentAt,
                        CanceledAt = s.CanceledAt
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<GetMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
