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
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List Maxio subscriptions for the authenticated shopper.
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ListMySubscriptionsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionBillingService billing, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(billing, user, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionBillingService billing)
        => HandleAsync(billing, new ClaimsPrincipal(), CancellationToken.None);

    public async Task<IResult> HandleAsync(
        ISubscriptionBillingService billing,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var customer = await BillingCustomerResolver.ResolveAsync(_userManager, user);
        if (customer is null)
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();
        var subscriptions = await billing.ListMySubscriptionsAsync(customer, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(Map));
        return Results.Ok(response);
    }

    private static UserSubscriptionDto Map(UserSubscription subscription)
        => new()
        {
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate,
            Reference = subscription.Reference
        };
}
