using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated shopper in a Maxio subscription plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionBillingService billing, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, billing, httpContext, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billing) =>
        HandleAsync(request, billing, new DefaultHttpContext(), CancellationToken.None);

    private async Task<IResult> HandleAsync(
        SubscribeRequest request,
        ISubscriptionBillingService billing,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var response = new SubscribeResponse(request.CorrelationId());
        var subscription = await billing.SubscribeAsync(userName, request.ProductHandle, cancellationToken);
        response.Subscription = ToDto(subscription);
        return Results.Created($"api/my-subscriptions", response);
    }

    internal static SubscriptionDto ToDto(ApplicationCore.Entities.SubscriptionAggregate.ShopperSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingAt = subscription.NextBillingAt
        };
}
