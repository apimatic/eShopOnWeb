using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    private readonly MaxioSubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(MaxioSubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (CancellationToken cancellationToken) =>
            await HandleRouteAsync(_service, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        return HandleRouteAsync(service, CancellationToken.None);
    }

    private async Task<IResult> HandleRouteAsync(MaxioSubscriptionService service, CancellationToken cancellationToken)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await service.GetMySubscriptionsAsync(principal, cancellationToken);
        return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions });
    }
}
