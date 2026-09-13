using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create a Subscription
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioService maxioService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, maxioService, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService)
    {
        throw new NotSupportedException("Use the overload with HttpContext to access the user identity.");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService, HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userEmail = httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? httpContext.User.FindFirstValue("email")
            ?? httpContext.User.Identity?.Name;

        if (string.IsNullOrEmpty(userEmail))
        {
            return Results.BadRequest(new { error = "Unable to determine user identity from token." });
        }

        var subscription = await maxioService.SubscribeAsync(userEmail, request.ProductHandle);

        response.Subscription = new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            ProductName = subscription.Product?.Name ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
            Currency = subscription.Currency
        };

        return Results.Created("api/my-subscriptions", response);
    }
}
