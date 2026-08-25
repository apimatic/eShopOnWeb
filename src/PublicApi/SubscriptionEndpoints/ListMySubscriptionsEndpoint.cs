using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class ListMySubscriptionsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (ClaimsPrincipal principal,
                    BillingCustomerFactory customerFactory,
                    ISubscriptionBillingService billingService,
                    CancellationToken cancellationToken) =>
                    await HandleAsync(principal, customerFactory, billingService, cancellationToken))
            .Produces<SubscriptionDetails[]>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAuthorization()
            .WithName("ListMySubscriptions")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        BillingCustomerFactory customerFactory,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken = default)
    {
        var user = await customerFactory.FindUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await billingService.ListSubscriptionsAsync(user.Id, cancellationToken));
        }
        catch (BillingException ex)
        {
            return BillingHttpResults.FromException(ex);
        }
    }
}
