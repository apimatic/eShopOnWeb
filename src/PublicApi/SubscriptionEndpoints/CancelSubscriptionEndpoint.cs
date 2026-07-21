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

public class CancelSubscriptionRequest : BaseRequest
{
    public bool EndOfPeriod { get; set; }
    public string? Reason { get; set; }

    internal int SubscriptionId { get; set; }
    internal string CustomerReference { get; set; } = string.Empty;
    internal bool IsAdmin { get; set; }
}

/// <summary>Cancels a subscription, immediately or at the end of the current period (UC4).</summary>
public class CancelSubscriptionEndpoint : IEndpoint<IResult, CancelSubscriptionRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId:int}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int subscriptionId, CancelSubscriptionRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.SubscriptionId = subscriptionId;
                request.CustomerReference = user.FindFirstValue(ClaimTypes.Name)!;
                request.IsAdmin = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<LifecycleActionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var response = new LifecycleActionResponse(request.CorrelationId());

        var subscription = await subscriptionService.CancelAsync(
            request.CustomerReference,
            request.SubscriptionId,
            request.EndOfPeriod,
            request.Reason,
            request.IsAdmin);

        response.Subscription = SubscriptionDto.FromDomain(subscription);

        return Results.Ok(response);
    }
}
