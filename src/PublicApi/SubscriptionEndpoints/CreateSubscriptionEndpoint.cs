using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record CreateSubscriptionRequest(string ProductHandle);

public sealed class CreateSubscriptionEndpoint : IEndpoint
{
    private readonly ISubscriptionService _subscriptionService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        ISubscriptionService subscriptionService,
        UserManager<ApplicationUser> userManager)
    {
        _subscriptionService = subscriptionService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", HandleAsync)
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Accepts<CreateSubscriptionRequest>("application/json")
            .Produces<CreateSubscriptionResponse>()
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ProductHandle)] = new[] { "ProductHandle is required." }
            });
        }

        var userName = context.User.Identity?.Name;
        var user = string.IsNullOrWhiteSpace(userName)
            ? null
            : await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await _subscriptionService.SubscribeAsync(
            SubscriptionIdentity.From(user),
            request.ProductHandle.Trim(),
            cancellationToken);
        if (result is null)
        {
            return Results.NotFound(new { message = "The requested subscription plan was not found." });
        }

        var response = new CreateSubscriptionResponse(
            SubscriptionDto.From(result.Subscription),
            result.Created);
        return result.Created
            ? Results.Created("/api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
