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

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (SubscribeRequest request,
                    ClaimsPrincipal principal,
                    BillingCustomerFactory customerFactory,
                    ISubscriptionBillingService billingService,
                    CancellationToken cancellationToken) =>
                    await HandleAsync(request, principal, customerFactory, billingService, cancellationToken))
            .Produces<SubscriptionDetails>()
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireAuthorization()
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
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
            var subscription = await billingService.SubscribeAsync(
                BillingCustomerFactory.Create(user),
                request.ProductHandle,
                cancellationToken);
            return Results.Ok(subscription);
        }
        catch (BillingException ex)
        {
            return BillingHttpResults.FromException(ex);
        }
    }
}
