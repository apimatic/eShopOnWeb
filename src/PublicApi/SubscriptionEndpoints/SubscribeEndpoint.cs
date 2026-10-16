using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> users, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, users, billing, cancellationToken);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billing) =>
        Task.FromResult(Results.Unauthorized());

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        ClaimsPrincipal user,
        UserManager<ApplicationUser> users,
        ISubscriptionBillingService billing,
        CancellationToken cancellationToken)
    {
        var shopper = await ShopperResolver.ResolveAsync(user, users);
        if (shopper.Error is not null)
            return shopper.Error;

        try
        {
            var result = await billing.SubscribeAsync(shopper.Account!, request.ProductHandle ?? "", cancellationToken);
            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = Map(result.Subscription)
            };
            return result.Created
                ? Results.Created("api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (SubscriptionBillingException ex)
        {
            return SubscriptionResults.FromBilling(ex);
        }
    }

    internal static ShopperSubscriptionDto Map(ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt
    };
}
