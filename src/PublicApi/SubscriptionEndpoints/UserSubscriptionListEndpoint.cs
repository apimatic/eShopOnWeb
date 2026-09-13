using System;
using System.Collections.Generic;
using System.Linq;
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
/// List User Subscriptions
/// </summary>
public class UserSubscriptionListEndpoint : IEndpoint<IResult, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioService maxioService, HttpContext httpContext) =>
            {
                return await HandleAsync(maxioService, httpContext);
            })
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioService maxioService)
    {
        throw new NotSupportedException("Use the overload with HttpContext to access the user identity.");
    }

    public async Task<IResult> HandleAsync(IMaxioService maxioService, HttpContext httpContext)
    {
        var response = new ListUserSubscriptionsResponse();

        var userEmail = httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? httpContext.User.FindFirstValue("email")
            ?? httpContext.User.Identity?.Name;

        if (string.IsNullOrEmpty(userEmail))
        {
            return Results.BadRequest(new { error = "Unable to determine user identity from token." });
        }

        var subscriptions = await maxioService.GetUserSubscriptionsAsync(userEmail);

        response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            ProductName = s.Product?.Name ?? string.Empty,
            ProductHandle = s.Product?.Handle ?? string.Empty,
            PriceInCents = s.ProductPriceInCents,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextAssessmentAt = s.NextAssessmentAt,
            ActivatedAt = s.ActivatedAt,
            CreatedAt = s.CreatedAt,
            CanceledAt = s.CanceledAt,
            CancelAtEndOfPeriod = s.CancelAtEndOfPeriod,
            Currency = s.Currency
        }).ToList();

        return Results.Ok(response);
    }
}
