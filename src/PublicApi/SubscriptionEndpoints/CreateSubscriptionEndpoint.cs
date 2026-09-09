using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent; safe against double-clicks)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService, UserManager<ApplicationUser>>
{
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(ILogger<CreateSubscriptionEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext httpContext,
                ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager) =>
            {
                request.Username = httpContext.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, subscriptionService, userManager);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService, UserManager<ApplicationUser> userManager)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var user = await userManager.FindByNameAsync(request.Username);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        return await SubscriptionEndpointHelpers.ExecuteAsync(
            _logger,
            async () =>
            {
                var details = await subscriptionService.SubscribeAsync(
                    new SubscribeCommand(user.Id, user.Email ?? string.Empty, request.ProductHandle));

                response.Subscription = Map(details);
                return Results.Json(response, statusCode: StatusCodes.Status201Created);
            });
    }

    internal static SubscriptionDto Map(SubscriptionDetails details) => new()
    {
        Id = details.Id,
        State = details.State,
        ProductHandle = details.ProductHandle,
        ProductName = details.ProductName,
        Price = details.Price,
        PriceInCents = details.PriceInCents,
        Interval = details.Interval,
        IntervalUnit = details.IntervalUnit,
        NextBillingDate = details.NextBillingDate,
        CurrentPeriodStart = details.CurrentPeriodStart,
        CurrentPeriodEnd = details.CurrentPeriodEnd,
        CreatedAt = details.CreatedAt,
        Reference = details.Reference,
        AlreadySubscribed = details.AlreadySubscribed
    };
}
