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

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult, SubscriptionService>
{
    private readonly SubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(SubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                CancellationToken cancellationToken) => await HandleAsync(user, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionService service)
    {
        var userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Results.Unauthorized();

        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(await service.ListMySubscriptionsAsync(userName, CancellationToken.None));
        return Results.Ok(response);
    }

    private async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Results.Unauthorized();

        var response = new MySubscriptionsResponse();
        response.Subscriptions.AddRange(await _service.ListMySubscriptionsAsync(userName, cancellationToken));
        return Results.Ok(response);
    }
}
