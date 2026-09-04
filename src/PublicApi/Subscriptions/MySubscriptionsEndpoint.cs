using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (HttpContext httpContext, ISubscriptionService service,
                CancellationToken cancellationToken) =>
            await HandleCoreAsync(httpContext, service, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService service)
        => HandleCoreAsync(_httpContextAccessor.HttpContext!, service, CancellationToken.None);

    private static async Task<IResult> HandleCoreAsync(HttpContext httpContext, ISubscriptionService service,
        CancellationToken cancellationToken)
    {
        var subscriptions = await service.GetMySubscriptionsAsync(httpContext.User, cancellationToken);
        return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions });
    }
}
