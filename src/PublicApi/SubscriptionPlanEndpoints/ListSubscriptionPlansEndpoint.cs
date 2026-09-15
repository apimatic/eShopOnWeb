using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>
/// List subscription plans available for purchase
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListSubscriptionPlansEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(billing);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        var response = new ListSubscriptionPlansResponse();
        var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var plans = await billing.ListPlansAsync(cancellationToken);

        response.Plans.AddRange(plans.Select(plan => new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description ?? string.Empty,
            Price = plan.Price,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit
        }));

        return Results.Ok(response);
    }
}

