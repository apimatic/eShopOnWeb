using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", HandleRoute)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(ISubscriptionService service) => HandleRoute(new ClaimsPrincipal(), service, CancellationToken.None);

    private static async Task<IResult> HandleRoute(ClaimsPrincipal user, ISubscriptionService service, CancellationToken cancellationToken)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await service.ListMySubscriptionsAsync(userName, cancellationToken);
        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions);
        return Results.Ok(response);
    }
}
