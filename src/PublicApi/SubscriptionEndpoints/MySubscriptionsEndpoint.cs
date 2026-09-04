using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly SubscriptionBillingService _billing;
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(SubscriptionBillingService billing, Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    {
        _billing = billing;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IResult> HandleAsync()
    {
        var context = _httpContextAccessor.HttpContext!;
        return Results.Ok(new SubscriptionListResponse
        {
            Subscriptions = new System.Collections.Generic.List<SubscriptionDto>(await _billing.ListMySubscriptionsAsync(context.User, context.RequestAborted))
        });
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            () => HandleAsync())
            .Produces<SubscriptionListResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}
