using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request,
             HttpContext httpContext,
             UserManager<ApplicationUser> userManager,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
                await HandleAsync(request, httpContext, userManager, billingService, cancellationToken))
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var username = httpContext.User.Identity?.Name;
        var user = username is null ? null : await userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new SubscriptionBillingException(HttpStatusCode.Unauthorized, "Unauthorized", "The authenticated user could not be resolved.");
        }

        var result = await billingService.SubscribeAsync(
            new BillingUser(user.Id, user.Email ?? string.Empty, user.FirstName, user.LastName),
            request.ProductHandle,
            cancellationToken);
        var response = new SubscribeResponse(result.Subscription, result.Created);
        return result.Created
            ? Results.Created($"/api/subscriptions/{result.Subscription.Reference}", response)
            : Results.Ok(response);
    }
}
