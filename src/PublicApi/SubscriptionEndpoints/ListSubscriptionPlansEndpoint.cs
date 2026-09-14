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
/// Lists the subscription plans a signed-in shopper can subscribe to.
/// Route: GET /api/subscription-plans
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly ISubscriptionBillingService _subscriptionBillingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListSubscriptionPlansEndpoint(
        ISubscriptionBillingService subscriptionBillingService,
        IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionBillingService = subscriptionBillingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] () => HandleAsync())
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var response = new SubscriptionPlanListResponse();
        var plans = await _subscriptionBillingService.ListAvailablePlansAsync(cancellationToken);
        response.SubscriptionPlans.AddRange(plans);
        return Results.Ok(response);
    }
}
