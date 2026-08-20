using System.Linq;
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
/// Lists Maxio subscriptions for the authenticated shopper.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billing, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billing, httpContext, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        HandleAsync(billing, new DefaultHttpContext(), CancellationToken.None);

    private async Task<IResult> HandleAsync(
        ISubscriptionBillingService billing,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await billing.GetSubscriptionsForUserAsync(userName, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.ToDto));
        return Results.Ok(response);
    }
}
