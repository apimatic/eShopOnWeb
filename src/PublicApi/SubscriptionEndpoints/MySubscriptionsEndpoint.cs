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

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioClient maxioClient, HttpContext httpContext) =>
            {
                var userName = httpContext.User.Identity?.Name ?? "unknown";

                var request = new MySubscriptionsRequest { CustomerReference = $"eshop-{userName}" };
                return await HandleAsync(request, maxioClient);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioClient maxioClient)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        try
        {
            var customer = await maxioClient.FindCustomerByReferenceAsync(request.CustomerReference!);

            if (customer is null)
            {
                response.Subscriptions = new List<SubscriptionDto>();
                response.IsSuccess = true;
                return Results.Ok(response);
            }

            var subscriptions = await maxioClient.GetCustomerSubscriptionsAsync(customer.Id);

            response.Subscriptions = subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                BalanceInCents = s.BalanceInCents,
                TotalRevenueInCents = s.TotalRevenueInCents,
                ProductPriceInCents = s.ProductPriceInCents,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                ActivatedAt = s.ActivatedAt,
                CreatedAt = s.CreatedAt,
                CanceledAt = s.CanceledAt,
                CancelAtEndOfPeriod = s.CancelAtEndOfPeriod,
                PaymentCollectionMethod = s.PaymentCollectionMethod,
                Currency = s.Currency,
                PlanName = s.Product?.Name,
                PlanHandle = s.Product?.Handle
            }).ToList();
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorMessage = $"Failed to retrieve subscriptions: {ex.Message}";
        }

        return Results.Ok(response);
    }
}
