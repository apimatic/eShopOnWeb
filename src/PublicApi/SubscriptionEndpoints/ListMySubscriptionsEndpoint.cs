using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, IMaxioService>
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
            (IMaxioService maxioService) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(), maxioService);
            })
            .Produces<ListMySubscriptionsRequest.Response>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioService maxioService)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new UnauthorizedAccessException("No HTTP context available");

        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? throw new UnauthorizedAccessException("User identity not found");

        var response = new ListMySubscriptionsRequest.Response(request.CorrelationId());

        var subscriptions = await maxioService.GetMySubscriptionsAsync(userId);
        response.Subscriptions = subscriptions;

        return Results.Ok(response);
    }
}
