using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a subscription plan. Idempotent: repeating the same
/// request for a plan the caller is already subscribed to returns the existing subscription.
/// </summary>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, ClaimsPrincipal, CreateSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, CreateSubscriptionRequest request, ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleCoreAsync(user, request, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ClaimsPrincipal request1, CreateSubscriptionRequest request2, ISubscriptionService request3)
    {
        return HandleCoreAsync(request1, request2, request3, CancellationToken.None);
    }

    private static async Task<IResult> HandleCoreAsync(
        ClaimsPrincipal user,
        CreateSubscriptionRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "A 'productHandle' identifying the plan to subscribe to is required."
            });
        }

        var result = await subscriptionService.SubscribeAsync(
            appUserId: user.Identity?.Name ?? string.Empty,
            email: user.Identity?.Name ?? string.Empty,
            productHandle: request.ProductHandle,
            cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription,
            Created = result.Created
        };

        return result.Created
            ? Results.Created("/api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
