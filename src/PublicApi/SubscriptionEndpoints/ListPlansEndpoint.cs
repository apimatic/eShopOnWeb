using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListPlansEndpoint : IEndpoint<IResult, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioClient maxioClient) =>
            {
                return await HandleAsync(maxioClient);
            })
            .Produces<ListPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioClient maxioClient)
    {
        var plans = await maxioClient.ListPlansAsync("eshop-subscribe");

        var dtos = new List<PlanDto>();
        foreach (var plan in plans)
        {
            dtos.Add(new PlanDto
            {
                Id = plan.Id,
                Name = plan.Name,
                Handle = plan.Handle,
                Description = plan.Description,
                Price = plan.Price,
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit
            });
        }

        var response = new ListPlansResponse { Plans = dtos };
        return Results.Ok(response);
    }
}
