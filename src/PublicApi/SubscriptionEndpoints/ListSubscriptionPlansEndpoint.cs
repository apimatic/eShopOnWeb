using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to subscribe to, sourced live from Maxio
/// (the billing system of record) rather than a local copy of the catalog.
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioClient maxioClient) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), maxioClient);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioClient maxioClient)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());
        var plans = await maxioClient.ListPlansAsync();

        response.Plans = plans.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Handle,
            Name = p.Name,
            PriceInCents = p.PriceInCents,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
            RequireCreditCard = p.RequireCreditCard
        }).ToList();

        return Results.Ok(response);
    }
}
