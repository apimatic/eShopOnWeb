using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListEndpoint : IEndpoint<IResult, MySubscriptionListRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user, IMaxioSubscriptionService service) =>
            {
                var req = new MySubscriptionListRequest();
                req.UserName = user.Identity?.Name ?? user.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "unknown";
                return await HandleAsync(req, service);
            })
            .Produces<MySubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(MySubscriptionListRequest request, IMaxioSubscriptionService service)
    {
        var response = new MySubscriptionListResponse(request.CorrelationId());
        try
        {
            response.Subscriptions = await service.GetMySubscriptionsAsync(request.UserName ?? "unknown");
        }
        catch (System.Exception ex)
        {
            return Results.Problem(title: "Subscription retrieval failed", detail: ex.Message, statusCode: 502);
        }
        return Results.Ok(response);
    }
}
