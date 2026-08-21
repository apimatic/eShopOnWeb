using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MySubscriptionListEndpoint :
    IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user,
                ISubscriptionBillingService billingService,
                CancellationToken cancellationToken) =>
                await HandleAsync(user, billingService, cancellationToken))
            .Produces<SubscriptionDto[]>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await billingService.ListForUserAsync(
            user.Identity?.Name ?? string.Empty,
            cancellationToken);
        return Results.Ok(subscriptions.Select(SubscriptionDto.From));
    }

    Task<IResult> IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService>.HandleAsync(
        ClaimsPrincipal user,
        ISubscriptionBillingService billingService) =>
        HandleAsync(user, billingService, CancellationToken.None);
}
