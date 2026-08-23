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

public sealed class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal principal,
                ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(
                    principal.Identity?.Name ?? string.Empty,
                    billingService,
                    cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        string userName,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var subscriptions = await billingService.GetSubscriptionsAsync(userName, cancellationToken);
        return Results.Ok(new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(SubscriptionMappings.ToDto).ToList()
        });
    }
}
