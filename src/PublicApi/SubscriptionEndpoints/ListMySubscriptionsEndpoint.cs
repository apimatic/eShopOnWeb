using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated user's subscriptions as recorded in Maxio.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ClaimsPrincipal>
{
    private readonly SubscriptionBillingService _billingService;

    public ListMySubscriptionsEndpoint(SubscriptionBillingService billingService)
    {
        _billingService = billingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    Task<IResult> IEndpoint<IResult, ClaimsPrincipal>.HandleAsync(ClaimsPrincipal user) =>
        HandleAsync(user, CancellationToken.None);

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        try
        {
            var response = new ListMySubscriptionsResponse();
            response.Subscriptions.AddRange(await _billingService.ListMySubscriptionsAsync(user, cancellationToken));
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return ListSubscriptionPlansEndpoint.MaxioError(ex);
        }
    }
}
