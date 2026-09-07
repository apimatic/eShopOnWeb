using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app, IServiceProvider services)
    {
        app.MapGet("api/subscription-plans",
            async () =>
            {
                var planRepository = services.GetRequiredService<IReadRepository<SubscriptionPlan>>();
                return await Handle(planRepository);
            })
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans");
    }

    private static async Task<IResult> Handle(IReadRepository<SubscriptionPlan> planRepository)
    {
        var response = new GetSubscriptionPlansResponse();

        try
        {
            var plans = await planRepository.ListAsync();
            response.Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description ?? "",
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToList();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        return Results.Ok(response);
    }
}

public class GetSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
