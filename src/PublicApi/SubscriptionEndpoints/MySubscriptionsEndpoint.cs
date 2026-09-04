using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, SubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (SubscriptionService service, CancellationToken cancellationToken) =>
                await HandleAsync(service, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscriptionService service)
    {
        return HandleAsync(service, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(SubscriptionService service, CancellationToken cancellationToken)
    {
        var subscriptions = await service.GetMySubscriptionsAsync(
            _httpContextAccessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal(),
            cancellationToken);
        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions);
        return Results.Ok(response);
    }
}
