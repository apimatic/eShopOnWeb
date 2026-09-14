using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Cancels one of the authenticated shopper's own subscriptions immediately.
/// </summary>
public class CancelSubscriptionEndpoint : IEndpoint<IResult, CancelSubscriptionRequest, IMaxioBillingService>
{
    private readonly ICurrentUserContext _currentUser;

    public CancelSubscriptionEndpoint(ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/{subscriptionId}/cancel",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int subscriptionId, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(new CancelSubscriptionRequest(subscriptionId), billingService);
            })
            .Produces<CancelSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelSubscriptionRequest request, IMaxioBillingService billingService)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await billingService.CancelSubscriptionAsync(user.Id, request.SubscriptionId);

        var response = new CancelSubscriptionResponse(request.CorrelationId())
        {
            Subscription = subscription
        };
        return Results.Ok(response);
    }
}
