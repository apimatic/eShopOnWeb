using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async (IMaxioSubscriptionService svc) =>
        {
            var plans = await svc.ListPlanOptionsAsync();
            var resp = new List<SubscriptionPlanResponse>();
            foreach (var p in plans)
            {
                resp.Add(new SubscriptionPlanResponse
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    Price = (double)p.Price,
                    Currency = p.Currency
                });
            }
            return Results.Ok(resp);
        })
        .Produces<List<SubscriptionPlanResponse>>(StatusCodes.Status200OK)
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IMaxioSubscriptionService svc)
        => Task.FromResult<IResult>(Results.Ok());
}

public class SubscriptionPlanResponse
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Price { get; set; }
    public string Currency { get; set; } = "USD";
}
