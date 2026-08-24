using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions as recorded in Maxio
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, SubscriptionBillingService>
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
            (SubscriptionBillingService billingService) =>
            {
                return await HandleAsync(billingService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscriptionBillingService billingService)
    {
        var username = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(await billingService.ListMySubscriptionsAsync(username));
        return Results.Ok(response);
    }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
