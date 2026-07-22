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

/// <summary>
/// UC1 — enrols the authenticated user in a plan. JWT-secured (mirrors <c>CreateCatalogItemEndpoint</c>).
/// The customer reference is the user's identity name (§4.4), resolved from the bearer token.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserName = user.Identity?.Name;
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrEmpty(request.UserName))
        {
            return Results.Unauthorized();
        }

        var subscription = await subscriptionService.SubscribeAsync(request.UserName, request.ProductHandle);
        return Results.Ok(new SubscribeResponse(request.CorrelationId()) { Subscription = subscription.ToDto() });
    }
}

public class SubscribeRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Populated server-side from the bearer token; not part of the request body.</summary>
    public string? UserName { get; set; }
}

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }

    public SubscribeResponse() { }

    public CustomerSubscriptionDto Subscription { get; set; } = new();
}
