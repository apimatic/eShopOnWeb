using System;
using System.Security.Claims;
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
            (CreateSubscriptionRequest request, ClaimsPrincipal principal,
                UserManager<ApplicationUser> userManager, ISubscriptionBillingService service,
                CancellationToken cancellationToken) =>
                await HandleAsync(request, principal, userManager, service, cancellationToken))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public static async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager, ISubscriptionBillingService service,
        CancellationToken cancellationToken)
    {
        var shopper = await SubscriptionEndpointSupport.GetShopperAsync(principal, userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await service.SubscribeAsync(shopper, request.ProductHandle, cancellationToken);
            var response = new CreateSubscriptionResponse
            {
                Subscription = result.Subscription,
                Created = result.Created
            };
            return result.Created
                ? Results.Created("/api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (Exception exception) when (exception is SubscriptionRequestException or
                                          SubscriptionConflictException or MaxioApiException)
        {
            return SubscriptionEndpointSupport.Error(exception);
        }
    }
}
