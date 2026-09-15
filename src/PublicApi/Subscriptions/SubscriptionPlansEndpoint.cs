using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, SubscriptionPlansRequest>
{
    private readonly SubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlansEndpoint(SubscriptionService subscriptionService, IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            () => await HandleAsync(new SubscriptionPlansRequest()))
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionPlansRequest request)
    {
        var response = new SubscriptionPlansResponse(request.CorrelationId());
        response.Plans.AddRange(await _subscriptionService.GetPlansAsync(
            _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None));
        return Results.Ok(response);
    }
}
