using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioService maxioService) =>
            {
                var plans = await maxioService.GetProductsAsync();
                var response = new SubscriptionPlansResponse
                {
                    Plans = plans.Select(p => new SubscriptionPlanDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Handle = p.Handle,
                        Price = (decimal)p.PriceInCents / 100,
                        BillingPeriod = p.IntervalUnit,
                    }).ToList()
                };
                return Results.Ok(response);
            })
           .Produces<SubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }
}

public class SubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string BillingPeriod { get; set; } = "month";
}
