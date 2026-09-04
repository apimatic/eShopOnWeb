using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioBillingClient>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlansEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioBillingClient maxio)
    {
        var plans = await maxio.GetPlansAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
        return Results.Ok(new SubscriptionPlansResponse
        {
            Plans = plans.Select(plan => new SubscriptionPlanResponse(
                plan.Handle,
                plan.Name,
                plan.Description,
                plan.PriceInCents,
                plan.Interval,
                plan.IntervalUnit)).ToArray()
        });
    }
}
