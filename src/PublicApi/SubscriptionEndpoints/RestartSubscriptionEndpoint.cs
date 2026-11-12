using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Restart one of the caller's cancelled subscriptions
/// </summary>
public class RestartSubscriptionEndpoint : IEndpoint<IResult, SubscriptionActionRequest, SubscriptionBilling>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/restart",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, ClaimsPrincipal user, SubscriptionBilling billing, HttpContext http) =>
            {
                return await HandleAsync(new SubscriptionActionRequest(Caller.UserName(user), subscriptionId), billing, http.RequestAborted);
            })
            .Produces<SubscriptionView>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionActionRequest request, SubscriptionBilling billing)
        => HandleAsync(request, billing, default);

    public async Task<IResult> HandleAsync(SubscriptionActionRequest request, SubscriptionBilling billing, System.Threading.CancellationToken ct)
    {
        var view = await billing.RestartAsync(request.UserName, request.SubscriptionId, ct);
        return Results.Ok(view);
    }
}
