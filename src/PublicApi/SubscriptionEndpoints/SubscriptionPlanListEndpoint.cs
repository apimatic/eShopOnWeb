using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, SubscriptionPlanListRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioSubscriptionService service) =>
            {
                return await HandleAsync(new SubscriptionPlanListRequest(), service);
            })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(SubscriptionPlanListRequest request, IMaxioSubscriptionService service)
    {
        var response = new SubscriptionPlanListResponse(request.CorrelationId());
        try
        {
            response.Plans = await service.GetPlansAsync();
        }
        catch (System.Exception ex)
        {
            return Results.Problem(title: "Subscription plan retrieval failed", detail: ex.Message, statusCode: 502);
        }
        return Results.Ok(response);
    }
}
