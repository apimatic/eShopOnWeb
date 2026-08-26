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
/// Lists the subscription plans available in the configured Maxio product family
/// </summary>
public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, IMaxioSubscriptionService>
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
            (IMaxioSubscriptionService subscriptionService) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), subscriptionService);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());
        response.Plans.AddRange(await subscriptionService.ListPlansAsync(ct));
        return Results.Ok(response);
    }
}
