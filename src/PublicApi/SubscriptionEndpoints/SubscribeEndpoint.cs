using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// POST /api/subscriptions — subscribes the authenticated caller to a plan. Ensures a single Maxio
/// customer exists for the user and enrolls them; idempotent, so a double-click yields the same
/// subscription rather than a duplicate. Returns the plan, price, state, and next billing date.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionBillingService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService service)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var cancellationToken = httpContext?.RequestAborted ?? CancellationToken.None;
        var subscriber = SubscriberIdentityFactory.FromPrincipal(httpContext!.User);

        var subscription = await service.SubscribeAsync(subscriber, request.PlanHandle ?? string.Empty, cancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDtoMapper.ToDto(subscription)
        };

        return Results.Ok(response);
    }
}
