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

public sealed class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (SubscribeRequest request, HttpContext httpContext,
                ISubscriptionService service, CancellationToken cancellationToken) =>
            await HandleCoreAsync(request, httpContext, service, cancellationToken))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService service)
        => HandleCoreAsync(request, _httpContextAccessor.HttpContext!, service, CancellationToken.None);

    private static async Task<IResult> HandleCoreAsync(SubscribeRequest request, HttpContext httpContext,
        ISubscriptionService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { error = "planHandle is required." });
        }

        var subscription = await service.SubscribeAsync(httpContext.User, request.PlanHandle, cancellationToken);
        return Results.Created("api/my-subscriptions", new SubscribeResponse { Subscription = subscription });
    }
}
