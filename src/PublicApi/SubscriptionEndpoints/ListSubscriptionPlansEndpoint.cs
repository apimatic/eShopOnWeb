using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioSubscriptionService service) =>
            {
                return await HandleAsync(new EmptyRequest(), service);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, MaxioSubscriptionService service)
    {
        try
        {
            var plans = await service.ListPlansAsync();
            var response = new ListSubscriptionPlansResponse
            {
                Plans = plans.Select(p => new PlanDto
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    PriceInCents = p.PriceInCents,
                    PriceInDollars = p.PriceInCents / 100m,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit
                }).ToList()
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class EmptyRequest { }

public class ListSubscriptionPlansResponse
{
    public List<PlanDto> Plans { get; set; } = new();
}

public class PlanDto
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long PriceInCents { get; set; }
    public decimal PriceInDollars { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}
