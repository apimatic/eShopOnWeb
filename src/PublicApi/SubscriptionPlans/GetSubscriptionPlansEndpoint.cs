using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlans;

public class GetSubscriptionPlansEndpoint : IEndpoint
{
    private readonly ISubscriptionPlanService _subscriptionPlanService;

    public GetSubscriptionPlansEndpoint(ISubscriptionPlanService subscriptionPlanService)
    {
        _subscriptionPlanService = subscriptionPlanService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async () =>
            {
                var plans = await _subscriptionPlanService.GetPlansAsync();

                var response = new GetSubscriptionPlansResponse()
                {
                    Plans = plans.Select(p => new SubscriptionPlanDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Handle = p.Handle,
                        Description = p.Description,
                        Price = p.Price,
                        IntervalUnit = p.IntervalUnit,
                        Interval = p.Interval
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
