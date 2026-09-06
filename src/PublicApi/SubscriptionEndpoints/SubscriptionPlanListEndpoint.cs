using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioService maxioService) =>
            {
                return await HandleAsync(maxioService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioService maxioService)
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await maxioService.GetSubscriptionPlansAsync();

        foreach (var plan in plans)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Handle = plan.Handle,
                Name = plan.Name,
                Description = plan.Description,
                PriceInDollars = plan.GetPriceInDollars(),
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit
            });
        }

        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}
