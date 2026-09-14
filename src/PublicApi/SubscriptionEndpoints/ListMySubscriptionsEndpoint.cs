using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class ListMySubscriptionsEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly AuthenticatedShopperResolver _shopperResolver;

    public ListMySubscriptionsEndpoint(
        ISubscriptionBillingService billingService,
        AuthenticatedShopperResolver shopperResolver)
    {
        _billingService = billingService;
        _shopperResolver = shopperResolver;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", (System.Delegate)((HttpContext context) => HandleAsync(context)))
            .Produces<ListMySubscriptionsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            });
    }

    public async Task<IResult> HandleAsync(HttpContext context)
    {
        var shopper = await _shopperResolver.ResolveAsync(context.User);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await _billingService.ListSubscriptionsAsync(
            shopper,
            context.RequestAborted);
        return Results.Ok(new ListMySubscriptionsResponse
        {
            Subscriptions = subscriptions.Select(x => x.ToDto()).ToList()
        });
    }
}
