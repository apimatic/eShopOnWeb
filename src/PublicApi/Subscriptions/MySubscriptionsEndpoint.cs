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

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(ISubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (HttpContext context, CancellationToken cancellationToken) =>
                HandleAsync(context.User, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        return await HandleAsync(principal, CancellationToken.None);
    }

    private async Task<IResult> HandleAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        return Results.Ok(await _service.GetMySubscriptionsAsync(principal, cancellationToken));
    }
}
