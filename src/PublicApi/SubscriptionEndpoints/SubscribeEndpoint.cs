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
/// Enrols the calling customer in a plan (UC1, the hero flow).
/// <para>
/// Safe to call repeatedly: the customer record is idempotent on the caller's identity, and a
/// repeat call for a plan the customer is already on returns the existing subscription rather than
/// creating a second one.
/// </para>
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionService subscriptionService) =>
                await HandleAsync(request, subscriptionService))
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        var userReference = _httpContextAccessor.CurrentUserReference();
        if (userReference is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("A plan handle is required.");
        }

        var subscription = await subscriptionService.SubscribeAsync(userReference, request.PlanHandle);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = subscription.ToDto()
        };

        return Results.Created($"api/subscriptions/{subscription.Id}", response);
    }
}
