using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated caller's subscriptions, read live from Maxio. Returns an empty list when the
/// user has no Maxio customer yet. JWT-authenticated; identity comes from the token.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
                await ExecuteAsync(user, billingService, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    // Required by IEndpoint; the mapped route binds identity + cancellation and calls ExecuteAsync.
    public Task<IResult> HandleAsync(ISubscriptionBillingService billingService) =>
        Task.FromResult(Results.Unauthorized());

    private static async Task<IResult> ExecuteAsync(
        ClaimsPrincipal? user,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var username = user?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscriptions = await billingService.GetSubscriptionsAsync(username, cancellationToken);
            return Results.Ok(new MySubscriptionsResponse { Subscriptions = subscriptions.ToList() });
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionErrorResults.From(ex);
        }
    }
}
