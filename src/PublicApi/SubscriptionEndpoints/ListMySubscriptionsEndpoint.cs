using System.Linq;
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

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, billing, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        throw new System.NotSupportedException();

    private async Task<IResult> HandleAsync(ClaimsPrincipal user, ISubscriptionBillingService billing, CancellationToken cancellationToken)
    {
        var customerReference = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            return Results.Unauthorized();
        }

        var subscriptions = await billing.ListMySubscriptionsAsync(customerReference, cancellationToken);
        var response = new ListMySubscriptionsResponse();
        response.Subscriptions.AddRange(subscriptions.Select(CreateSubscriptionEndpoint.Map));
        return Results.Ok(response);
    }
}
