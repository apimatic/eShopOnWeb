using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List available subscription plans from Maxio
/// </summary>
public class SubscriptionPlanListEndpoint : IEndpoint<IResult, IMaxioClient>
{
    private readonly MaxioConfiguration _config;

    public SubscriptionPlanListEndpoint(IOptions<MaxioConfiguration> config)
    {
        _config = config.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioClient maxioClient) =>
            {
                return await HandleAsync(maxioClient);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioClient maxioClient)
    {
        var plans = await maxioClient.ListPlansAsync(_config.ProductFamilyHandle);

        var dtos = plans.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Name = p.Name,
            Handle = p.Handle,
            Description = p.Description,
            Price = p.PriceInCents / 100m,
            IntervalUnit = p.IntervalUnit,
            Interval = p.Interval
        }).ToList();

        var response = new ListSubscriptionPlansResponse { Plans = dtos };
        return Results.Ok(response);
    }
}
