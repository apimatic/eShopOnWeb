using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, IMaxioSubscriptionService service) => await HandleAsync(request, service))
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, IMaxioSubscriptionService service)
    {
        var subscription = await service.SubscribeAsync(request.PlanHandle);
        return subscription is null
            ? Results.BadRequest(new { message = "The requested subscription plan is not available." })
            : Results.Ok(new SubscribeResponse { Subscription = subscription });
    }
}
