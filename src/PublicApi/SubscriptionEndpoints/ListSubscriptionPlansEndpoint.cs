using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// GET /api/subscription-plans — lists the subscribable plans in the configured Maxio product family.
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
            (ISubscriptionBillingService billing) => await HandleAsync(billing))
            .Produces<SubscriptionPlansResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        var http = _httpContextAccessor.HttpContext!;
        try
        {
            var plans = await billing.GetPlansAsync(http.RequestAborted);
            var response = new SubscriptionPlansResponse
            {
                Truncated = plans.Truncated,
                Plans = plans.Plans.Select(p => new SubscriptionPlanDto
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Price = p.PriceInCents / 100m,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit,
                    PaymentMethodRequired = p.PaymentMethodRequired
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return BillingResultMapper.ToProblem(ex);
        }
    }
}
