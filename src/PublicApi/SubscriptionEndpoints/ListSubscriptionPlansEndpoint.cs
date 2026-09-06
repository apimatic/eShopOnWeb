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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public partial class ListSubscriptionPlansEndpoint
{
    public static void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioService maxioService) =>
            {
                var products = await maxioService.GetProductsAsync();
                var response = new ListResponse(Guid.NewGuid())
                {
                    Plans = products
                        .Select(p => new SubscriptionPlanDto
                        {
                            Id = p.Id,
                            Name = p.Name,
                            Handle = p.Handle,
                            Price = p.PriceInCents / 100m,
                            Interval = p.Interval,
                            IntervalUnit = p.IntervalUnit
                        })
                        .ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ListResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
