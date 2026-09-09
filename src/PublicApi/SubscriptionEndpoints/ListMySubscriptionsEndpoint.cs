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

/// <summary>
/// Lists the authenticated user's subscriptions. Returns an empty list when the user has not
/// subscribed to anything yet (no Maxio customer exists).
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionBillingService billingService, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billingService, user, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithName("subscriptions.list-mine");
    }

    public async Task<IResult> HandleAsync(
        ISubscriptionBillingService billingService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var reference = SubscriptionMappings.ResolveCustomerReference(user);

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await billingService.GetSubscriptionsForUserAsync(reference, cancellationToken);
        response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();

        return Results.Ok(response);
    }

    // Satisfies IEndpoint<IResult, TParam1, TParam2>.
    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService, ClaimsPrincipal user)
        => HandleAsync(billingService, user, default);
}
