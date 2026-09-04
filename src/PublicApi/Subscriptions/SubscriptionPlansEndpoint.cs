using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioSubscriptionService service, HttpContext httpContext) =>
                await HandleAsync(service, httpContext))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService service)
    {
        return await HandleAsync(service, new DefaultHttpContext());
    }

    private static async Task<IResult> HandleAsync(IMaxioSubscriptionService service, HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(httpContext.User.Identity?.Name))
            return Results.Unauthorized();

        var plans = await service.GetPlansAsync(httpContext.RequestAborted);
        return Results.Ok(new SubscriptionPlansResponse { Plans = plans });
    }
}
