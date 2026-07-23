using System.Security.Claims;
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
/// Enrolls the authenticated user in a plan (UC1). Repeating the call while an active subscription
/// on the same plan exists returns that subscription rather than creating a second one.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request,
             ClaimsPrincipal user,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                request.UserReference = SubscriptionUser.ReferenceOf(user);
                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        return HandleAsync(request, subscriptionService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("A plan handle is required.");
        }

        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await subscriptionService.SubscribeAsync(request.UserReference,
            request.PlanHandle,
            cancellationToken);

        response.Subscription = SubscriptionDto.FromSubscription(subscription);

        return Results.Created($"api/subscriptions/{subscription.Id}", response);
    }
}
