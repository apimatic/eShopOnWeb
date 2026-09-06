using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, IMaxioSubscriptionService>
{
    private readonly ILogger<MySubscriptionsEndpoint> _logger;

    public MySubscriptionsEndpoint(ILogger<MySubscriptionsEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext context, IMaxioSubscriptionService service) =>
            {
                try
                {
                    var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                                context.User.FindFirst("sub")?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        return Results.BadRequest(new ProblemDetails
                        {
                            Title = "User identification failed",
                            Detail = "Could not identify user from token claims"
                        });
                    }

                    var subscriptions = await service.GetUserSubscriptionsAsync(userId);
                    var response = new MySubscriptionsResponse
                    {
                        Subscriptions = subscriptions.Select(s => new UserSubscriptionDto
                        {
                            Id = s.Id,
                            State = s.State,
                            PriceInCents = s.PriceInCents,
                            CurrentPeriodStartsAt = s.CurrentPeriodStartsAt,
                            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                            NextBillingAt = s.NextBillingAt
                        }).ToList()
                    };
                    return Results.Ok(response);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError($"Failed to retrieve subscriptions: {ex.Message}");
                    return Results.Problem(title: "Failed to retrieve subscriptions", detail: ex.Message, statusCode: 500);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Unexpected error retrieving subscriptions: {ex}");
                    return Results.Problem(title: "Failed to retrieve subscriptions", detail: ex.Message, statusCode: 500);
                }
            })
           .RequireAuthorization()
           .Produces<MySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioSubscriptionService service)
    {
        throw new NotImplementedException("This endpoint uses inline lambda handling");
    }
}

public class UserSubscriptionDto
{
    public long Id { get; set; }
    public string? State { get; set; }
    public long PriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}

public class MySubscriptionsResponse
{
    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
