using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsyncInternal)
           .Produces<ListSubscriptionPlansResponse>()
           .WithName("ListSubscriptionPlans")
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync() => throw new NotImplementedException();

    private async Task<IResult> HandleAsyncInternal(CatalogContext catalogContext)
    {
        var plans = await catalogContext.SubscriptionPlans
            .OrderBy(p => p.Name)
            .ToListAsync();

        var dtos = plans.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Handle = p.Handle,
            Name = p.Name,
            Description = p.Description,
            PriceInCents = p.PriceInCents,
            IntervalValue = p.IntervalValue,
            IntervalUnit = p.IntervalUnit
        }).ToList();

        var response = new ListSubscriptionPlansResponse { Plans = dtos };
        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
