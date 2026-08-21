using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateSubscriptionRequest request,
                    ISubscriptionService subscriptionService,
                    UserManager<ApplicationUser> userManager,
                    HttpContext context) =>
                    await HandleAsync(request, subscriptionService, userManager, context))
            .Produces<SubscriptionDto>(StatusCodes.Status200OK)
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ProductHandle)] = new[] { "ProductHandle is required." }
            });
        }

        var user = await BillingUserResolver.ResolveAsync(context.User, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.SubscribeAsync(
            user,
            request.ProductHandle.Trim(),
            context.RequestAborted);
        var response = SubscriptionDto.From(result.Subscription);

        return result.Created
            ? Results.Created("/api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
