using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List subscriptions for the authenticated user
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest>
{
    private readonly IMaxioService _maxioService;
    private HttpContext? _httpContext;

    public ListMySubscriptionsEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext) =>
            {
                _httpContext = httpContext;
                return await HandleAsync(new ListMySubscriptionsRequest());
            })
            .WithName("ListMySubscriptions")
            .Produces<ListMySubscriptionsResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request)
    {
        try
        {
            if (_httpContext == null)
                throw new InvalidOperationException("HttpContext not available");

            // Extract user ID from JWT claims
            var userId = _httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? _httpContext.User.FindFirstValue("sub")
                ?? throw new UnauthorizedAccessException("User ID not found in token");

            var subscriptions = await _maxioService.ListUserSubscriptionsAsync(userId, CancellationToken.None);

            var response = new ListMySubscriptionsResponse(request.CorrelationId())
            {
                Subscriptions = subscriptions
            };

            return Results.Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
