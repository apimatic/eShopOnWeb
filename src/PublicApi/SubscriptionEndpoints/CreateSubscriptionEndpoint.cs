using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Creates a Maxio subscription for the authenticated shopper
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateShopSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, CreateShopSubscriptionRequest request, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(httpContext, request, billing, cancellationToken);
            })
            .Produces<CreateShopSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateShopSubscriptionRequest request, ISubscriptionBillingService billing)
        => await HandleAsync(new DefaultHttpContext(), request, billing, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        HttpContext httpContext,
        CreateShopSubscriptionRequest request,
        ISubscriptionBillingService billing,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.Identity?.Name;
        var response = new CreateShopSubscriptionResponse(request.CorrelationId());
        var created = await billing.SubscribeAsync(userId!, request.ProductHandle, cancellationToken);
        response.Subscription = Map(created);
        return Results.Created($"api/subscriptions/{created.Id}", response);
    }

    private static ShopSubscriptionDto Map(ShopSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle ?? string.Empty,
        ProductName = subscription.ProductName ?? string.Empty,
        Price = subscription.Price,
        State = subscription.State ?? string.Empty,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}
