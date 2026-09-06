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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists subscriptions for the current user
/// </summary>
public class ListUserSubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioSubscriptionService subscriptionService, HttpContext context) =>
            {
                var request = new EmptyRequest();
                request.UserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<ListUserSubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IMaxioSubscriptionService subscriptionService)
    {
        var response = new ListUserSubscriptionsResponse(request.CorrelationId());

        var subscriptions = await subscriptionService.GetUserSubscriptionsAsync(request.UserId);
        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            CustomerId = s.CustomerId,
            ProductName = s.ProductName,
            ProductPriceInCents = s.ProductPriceInCents,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            NextAssessmentAt = s.NextAssessmentAt,
            ActivatedAt = s.ActivatedAt,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        }));

        return Results.Ok(response);
    }
}
