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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(ILogger<CreateSubscriptionEndpoint> logger)
    {
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext context, IMaxioSubscriptionService service) =>
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

                    if (string.IsNullOrEmpty(request.ProductHandle))
                    {
                        return Results.BadRequest(new ProblemDetails
                        {
                            Title = "Invalid request",
                            Detail = "ProductHandle is required"
                        });
                    }

                    var email = context.User.FindFirst(ClaimTypes.Email)?.Value;
                    var firstName = context.User.FindFirst("given_name")?.Value ?? "Customer";
                    var lastName = context.User.FindFirst("family_name")?.Value ?? "";

                    if (string.IsNullOrEmpty(email))
                    {
                        return Results.BadRequest(new ProblemDetails
                        {
                            Title = "Invalid user profile",
                            Detail = "User email is required"
                        });
                    }

                    var result = await service.SubscribeAsync(userId, email, firstName, lastName, request.ProductHandle);

                    var response = new CreateSubscriptionResponse
                    {
                        SubscriptionId = result.SubscriptionId,
                        State = result.State,
                        ProductHandle = result.ProductHandle,
                        PriceInCents = result.PriceInCents,
                        CurrentPeriodStartsAt = result.CurrentPeriodStartsAt,
                        CurrentPeriodEndsAt = result.CurrentPeriodEndsAt,
                        NextBillingAt = result.NextBillingAt
                    };

                    return Results.Created($"/api/subscriptions/{result.SubscriptionId}", response);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError($"Subscription error: {ex.Message}");
                    return Results.BadRequest(new ProblemDetails
                    {
                        Title = "Subscription creation failed",
                        Detail = ex.Message
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Unexpected error creating subscription: {ex}");
                    return Results.Problem(title: "Failed to create subscription", detail: ex.Message, statusCode: 500);
                }
            })
           .RequireAuthorization()
           .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
           .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
           .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError)
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService service)
    {
        throw new NotImplementedException("This endpoint uses inline lambda handling");
    }
}

public class CreateSubscriptionRequest
{
    public string? ProductHandle { get; set; }
}

public class CreateSubscriptionResponse
{
    public long SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public long PriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}
