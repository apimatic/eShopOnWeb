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

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioService maxioService) =>
            {
                return await HandleAsync(maxioService);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioService maxioService)
    {
        var products = await maxioService.GetProductsAsync();

        var plans = products.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Name = p.Name,
            Handle = p.Handle,
            Description = p.Description,
            PricePerMonth = p.PriceInCents / 100m,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
        }).ToList();

        return Results.Ok(new ListSubscriptionPlansResponse { Plans = plans });
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
