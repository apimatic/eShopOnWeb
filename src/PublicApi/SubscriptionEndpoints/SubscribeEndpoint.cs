using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC1: enroll the authenticated caller in a plan. Mirrors CreateCatalogItemEndpoint's shape.</summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                // Never trust a client-supplied user reference — always derive it from the token.
                request.UserReference = httpContext.User.Identity!.Name!;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());
        var subscription = await subscriptionService.SubscribeAsync(request.UserReference, request.UserReference, request.PlanHandle);
        response.Subscription = SubscriptionDto.FromDomain(subscription);
        return Results.Ok(response);
    }
}
