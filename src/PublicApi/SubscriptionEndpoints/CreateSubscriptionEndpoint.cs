using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (HttpContext context,
                    SubscribeRequest request,
                    ISubscriptionBillingService billing,
                    SubscriptionUserResolver userResolver,
                    CancellationToken cancellationToken) =>
                    await HandleAsync(context, request, billing, userResolver, cancellationToken))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        HttpContext context,
        SubscribeRequest request,
        ISubscriptionBillingService billing,
        SubscriptionUserResolver userResolver,
        CancellationToken cancellationToken)
    {
        var user = await userResolver.ResolveAsync(context);
        var result = await billing.SubscribeAsync(user, request.ProductHandle, cancellationToken);

        if (result.IsPending)
        {
            return Results.Accepted("/api/my-subscriptions", new SubscribeResponse
            {
                Status = "pending",
                Code = result.StatusCode
            });
        }

        var subscription = result.Subscription!.ToDto();
        return Results.Created(
            $"/api/subscriptions/{Uri.EscapeDataString(subscription.Reference)}",
            new SubscribeResponse
            {
                Status = "succeeded",
                Subscription = subscription
            });
    }
}
