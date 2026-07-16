using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC1: enrolls the authenticated caller in the requested plan.</summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionEndpointContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, SubscriptionEndpointContext.From(subscriptionService, user));
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionEndpointContext context)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await context.SubscriptionService.SubscribeAsync(context.UserId, context.UserId, request.ProductHandle);
        response.Subscription = SubscriptionMapping.ToDto(subscription);

        return Results.Ok(response);
    }
}
