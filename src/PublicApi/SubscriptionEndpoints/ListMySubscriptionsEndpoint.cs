using System.Linq;
using System.Security.Claims;
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
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, UserManager<ApplicationUser> users, ISubscriptionBillingService billing, CancellationToken cancellationToken) =>
            {
                var shopper = await ShopperResolver.ResolveAsync(user, users);
                if (shopper.Error is not null)
                    return shopper.Error;

                try
                {
                    var subscriptions = await billing.ListMineAsync(shopper.Account!, cancellationToken);
                    return Results.Ok(new ListMySubscriptionsResponse
                    {
                        Subscriptions = subscriptions.Select(SubscribeEndpoint.Map).ToList()
                    });
                }
                catch (SubscriptionBillingException ex)
                {
                    return SubscriptionResults.FromBilling(ex);
                }
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing) =>
        Task.FromResult(Results.Unauthorized());
}
