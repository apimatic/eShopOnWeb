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

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, SubscriptionService>
{
    private readonly SubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(SubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SubscribeRequest request,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) => await HandleAsync(request, user, cancellationToken))
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, SubscriptionService service)
    {
        var userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Results.Unauthorized();

        var subscription = await service.SubscribeAsync(userName, request.PlanHandle, CancellationToken.None);
        return Results.Ok(new SubscribeResponse(request.CorrelationId()) { Subscription = subscription });
    }

    private async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
            return Results.BadRequest(new { message = "planHandle is required." });

        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Results.Unauthorized();

        var subscription = await _service.SubscribeAsync(userName, request.PlanHandle, cancellationToken);
        return Results.Ok(new SubscribeResponse(request.CorrelationId()) { Subscription = subscription });
    }
}
