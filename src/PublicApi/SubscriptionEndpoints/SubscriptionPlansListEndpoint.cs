using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscribe-able subscription plans (GET api/subscription-plans).
/// </summary>
public class SubscriptionPlansListEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlansListEndpoint(
        IMaxioSubscriptionService subscriptionService,
        IHttpContextAccessor httpContextAccessor)
    {
        _subscriptionService = subscriptionService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync();
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var plans = await _subscriptionService.ListPlansAsync(ct);

        var response = new ListSubscriptionPlansResponse();
        response.SubscriptionPlans.AddRange(plans);
        return Results.Ok(response);
    }
}
