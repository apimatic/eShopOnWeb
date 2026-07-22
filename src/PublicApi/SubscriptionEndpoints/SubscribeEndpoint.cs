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
/// Enrol the signed in user in a plan (UC1, the hero flow). Repeating the call returns the existing
/// live subscription rather than enrolling a second time.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpRequest httpRequest, ClaimsPrincipal user, ISubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                var request = SubscribeRequest.From(await SubscriptionRequestBody.ReadAsync(httpRequest, cancellationToken));
                return await HandleAsync(request, user, subscriptionService, cancellationToken);
            })
            .Accepts<SubscribeRequest>("application/json")
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
        => HandleAsync(request, new ClaimsPrincipal(), subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request, ClaimsPrincipal user,
        ISubscriptionService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await subscriptionService.SubscribeAsync(user.ToSubscriptionActor(),
            request.PlanHandle, cancellationToken);

        response.Subscription = subscription.ToDto();

        return Results.Created($"api/subscriptions/{subscription.Id}", response);
    }
}
