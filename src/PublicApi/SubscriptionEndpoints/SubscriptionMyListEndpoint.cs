using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionMyListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, Maxio.MaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (Maxio.MaxioClient maxioClient, HttpContext httpContext) =>
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.Name)
                    ?? httpContext.User.FindFirstValue("sub")
                    ?? throw new InvalidOperationException("Unable to determine user identity from JWT.");
                var request = new ListMySubscriptionsRequest { UserId = userId };
                return await HandleAsync(request, maxioClient);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request, Maxio.MaxioClient maxioClient)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var customer = await maxioClient.FindCustomerByReferenceAsync(request.UserId);
        if (customer == null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await maxioClient.ListSubscriptionsForCustomerAsync(customer.Id);

        foreach (var sub in subscriptions)
        {
            response.Subscriptions.Add(new SubscriptionDto
            {
                Id = sub.Id,
                State = sub.State,
                ProductId = sub.ProductId,
                ProductHandle = sub.ProductHandle ?? string.Empty,
                ProductName = sub.Product?.Name ?? string.Empty,
                CustomerId = sub.CustomerId,
                PriceInCents = sub.ProductPriceInCents,
                CurrentBillingAmountInCents = sub.CurrentBillingAmountInCents,
                Currency = sub.Currency ?? "USD",
                CreatedAt = sub.CreatedAt,
                NextAssessmentAt = sub.NextAssessmentAt,
                CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
                ActivatedAt = sub.ActivatedAt
            });
        }

        return Results.Ok(response);
    }
}
