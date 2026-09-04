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

public sealed class SubscriptionCreateRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public sealed class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscriptionCreateRequest>
{
    private readonly ISubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionCreateEndpoint(ISubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (SubscriptionCreateRequest request, HttpContext context, CancellationToken cancellationToken) =>
                HandleAsync(request, context.User, cancellationToken))
            .Produces<MySubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionCreateRequest request)
    {
        var principal = _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        return await HandleAsync(request, principal, CancellationToken.None);
    }

    private async Task<IResult> HandleAsync(SubscriptionCreateRequest request, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var subscription = await _service.SubscribeAsync(principal, request.PlanHandle, cancellationToken);
        return Results.Created("api/my-subscriptions", subscription);
    }
}
