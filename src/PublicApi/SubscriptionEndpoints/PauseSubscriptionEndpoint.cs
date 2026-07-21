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

public class LifecycleActionRequest : BaseRequest
{
    internal int SubscriptionId { get; set; }
    internal string CustomerReference { get; set; } = string.Empty;
    internal bool IsAdmin { get; set; }
}

public class LifecycleActionResponse : BaseResponse
{
    public LifecycleActionResponse(Guid correlationId) : base(correlationId) { }
    public LifecycleActionResponse() { }

    public SubscriptionDto Subscription { get; set; } = null!;
}

/// <summary>Pauses an active subscription (UC4).</summary>
public class PauseSubscriptionEndpoint : IEndpoint<IResult, LifecycleActionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/pause",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                var request = new LifecycleActionRequest
                {
                    SubscriptionId = subscriptionId,
                    CustomerReference = user.FindFirstValue(ClaimTypes.Name)!,
                    IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS),
                };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleActionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(LifecycleActionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleActionResponse(request.CorrelationId());
        var subscription = await subscriptionService.PauseAsync(request.CustomerReference, request.SubscriptionId, request.IsAdmin);
        response.Subscription = SubscriptionDto.FromDomain(subscription);
        return Results.Ok(response);
    }
}
