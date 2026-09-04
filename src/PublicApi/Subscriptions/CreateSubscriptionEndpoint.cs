using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioSubscriptionService>
{
    private readonly MaxioSubscriptionService _service;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(MaxioSubscriptionService service, IHttpContextAccessor httpContextAccessor)
    {
        _service = service;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest request, CancellationToken cancellationToken) =>
            await HandleRouteAsync(request, _service, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        MaxioSubscriptionService service)
    {
        return HandleRouteAsync(request, service, CancellationToken.None);
    }

    private async Task<IResult> HandleRouteAsync(
        CreateSubscriptionRequest request,
        MaxioSubscriptionService service,
        CancellationToken cancellationToken)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        var subscription = await service.SubscribeAsync(principal, request.PlanHandle, cancellationToken);
        return Results.Ok(new CreateSubscriptionResponse { Subscription = subscription });
    }
}
