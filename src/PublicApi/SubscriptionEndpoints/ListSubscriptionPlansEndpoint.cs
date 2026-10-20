using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans on offer
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, HttpContext, SubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, SubscriptionService service) =>
            {
                return await HandleAsync(http, service);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, SubscriptionService service)
    {
        var response = new ListSubscriptionPlansResponse(new SubscribeRequest().CorrelationId());

        var result = await service.ListPlansAsync(http.RequestAborted);
        response.Plans = result.Plans.Select(SubscriptionPlanDto.From).ToList();
        response.IsTruncated = result.IsTruncated;

        return Results.Ok(response);
    }
}
