using System;
using System.Collections.Generic;
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

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsEndpoint.ListMySubscriptionsRequest, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IMaxioClient maxioClient) =>
            {
                var request = new ListMySubscriptionsRequest
                {
                    UserId = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty
                };
                return await HandleAsync(request, maxioClient);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, IMaxioClient maxioClient)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        if (string.IsNullOrEmpty(request.UserId))
        {
            return Results.Unauthorized();
        }

        var customer = await maxioClient.FindCustomerByReferenceAsync(request.UserId);
        if (customer == null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await maxioClient.ListSubscriptionsByCustomerAsync(customer.Id);

        foreach (var sub in subscriptions)
        {
            response.Subscriptions.Add(new SubscriptionDto
            {
                Id = sub.Id,
                State = sub.State,
                BalanceInCents = sub.BalanceInCents,
                TotalRevenueInCents = sub.TotalRevenueInCents,
                ProductPriceInCents = sub.ProductPriceInCents,
                CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                NextAssessmentAt = sub.NextAssessmentAt,
                ActivatedAt = sub.ActivatedAt,
                CanceledAt = sub.CanceledAt,
                ExpiresAt = sub.ExpiresAt,
                CreatedAt = sub.CreatedAt,
                Currency = sub.Currency,
                ProductName = sub.Product?.Name,
                ProductHandle = sub.Product?.Handle,
                Reference = sub.Reference
            });
        }

        return Results.Ok(response);
    }

    public class ListMySubscriptionsRequest : BaseRequest
    {
        internal string UserId { get; set; } = string.Empty;
    }
}
