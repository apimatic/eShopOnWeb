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
/// GET /api/my-subscriptions — lists the caller's subscriptions as reflected in Maxio.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing) => await HandleAsync(billing))
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ISubscriptionBillingService billing)
    {
        var http = _httpContextAccessor.HttpContext!;

        var identity = await SubscriberIdentityResolver.ResolveAsync(http);
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billing.GetSubscriptionsAsync(identity, http.RequestAborted);
            var response = new MySubscriptionsResponse
            {
                Subscriptions = subscriptions.Select(BillingResultMapper.ToDto).ToList()
            };
            return Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return BillingResultMapper.ToProblem(ex);
        }
    }
}
