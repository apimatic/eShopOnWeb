using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, object, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioClient maxio) =>
        {
            return await HandleAsync(new {}, maxio);
        })
        .Produces<ListSubscriptionPlanResponse>()
        .RequireAuthorization()
        .WithTags("SubscriptionPlanEndpoints");
    }

    public async Task<IResult> HandleAsync(object request, IMaxioClient maxio)
    {
        var plans = await maxio.ListPlansAsync();
        var response = new ListSubscriptionPlanResponse(Guid.NewGuid())
        {
            Plans = plans.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                PriceInCents = p.PriceInCents,
                Price = p.PriceInCents / 100.0m,
                Interval = p.IntervalUnit,
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListSubscriptionPlanResponse : BaseResponse
{
    public ListSubscriptionPlanResponse(Guid correlationId) : base(correlationId) { }
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string Interval { get; set; } = "";
}
