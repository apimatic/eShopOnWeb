using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanListEndpoint : IEndpoint<IResult>
{
    private readonly SubscriptionBillingService _billing;
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;

    public SubscriptionPlanListEndpoint(SubscriptionBillingService billing, Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    {
        _billing = billing;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IResult> HandleAsync()
    {
        var context = _httpContextAccessor.HttpContext!;
        return Results.Ok(new SubscriptionPlanListResponse
        {
            Plans = (await _billing.ListPlansAsync(context.RequestAborted)).AsList()
        });
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", () => HandleAsync())
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme })
            .Produces<SubscriptionPlanListResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}

internal static class SubscriptionListExtensions
{
    public static System.Collections.Generic.List<T> AsList<T>(this System.Collections.Generic.IReadOnlyList<T> values) => new(values);
}
