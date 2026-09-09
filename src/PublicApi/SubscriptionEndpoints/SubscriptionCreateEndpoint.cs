using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated caller in the plan identified by its stable
/// handle (POST /api/subscriptions). Idempotent: a repeated call returns the
/// caller's existing subscription instead of creating a second one.
/// </summary>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, ISubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionCreateEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                return await HandleCoreAsync(request, user, subscriptionService);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .ProducesValidationProblem()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ISubscriptionService subscriptionService)
    {
        return HandleCoreAsync(null, null, subscriptionService);
    }

    private async Task<IResult> HandleCoreAsync(CreateSubscriptionRequest? request, ClaimsPrincipal? user, ISubscriptionService subscriptionService)
    {
        var username = user?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["productHandle"] = new[] { "productHandle is required." }
            });
        }

        var applicationUser = await _userManager.FindByNameAsync(username);
        var email = applicationUser?.Email;

        var details = await subscriptionService.SubscribeAsync(
            new SubscribeCommand(username, username, email, request.ProductHandle.Trim()),
            CancellationToken.None);

        if (details is null)
        {
            return Results.NotFound(new
            {
                message = $"No subscription plan with handle '{request.ProductHandle}' exists."
            });
        }

        var response = new CreateSubscriptionResponse(Guid.NewGuid())
        {
            Subscription = ToDto(details)
        };

        return Results.Ok(response);
    }

    internal static SubscriptionItemDto ToDto(ApplicationCore.Models.SubscriptionDetails details) =>
        new()
        {
            MaxioSubscriptionId = details.MaxioSubscriptionId,
            MaxioReference = details.MaxioReference,
            MaxioCustomerId = details.MaxioCustomerId,
            State = details.State,
            ProductHandle = details.ProductHandle,
            ProductName = details.ProductName,
            PriceInCents = details.PriceInCents,
            Price = details.Price,
            NextBillingAt = details.NextBillingAt,
            ActivatedAt = details.ActivatedAt,
            CanceledAt = details.CanceledAt,
            CreatedAt = details.CreatedAt
        };
}
