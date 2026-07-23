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
/// Lists the caller's subscriptions (UC1, step 7). Administrators may list another user's by
/// passing <c>?userReference=</c>.
/// </summary>
public class MySubscriptionsEndpoint : SubscriptionEndpointBase,
    IEndpoint<IResult, ISubscriptionService>
{
    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        var userReference = ResolveUserReference(RequestedUserReferenceFromQuery());
        if (userReference is null)
        {
            return Denied();
        }

        var response = new MySubscriptionsResponse();

        var subscriptions = await subscriptionService.GetSubscriptionsForUserAsync(userReference);
        response.Subscriptions = subscriptions.ToDtos();

        return Results.Ok(response);
    }
}
