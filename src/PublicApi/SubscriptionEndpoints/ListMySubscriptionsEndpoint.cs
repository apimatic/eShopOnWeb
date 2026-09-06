using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioService maxioService, HttpContext httpContext) =>
            {
                var response = new ListMySubscriptionsResponse();

                var userEmail = httpContext.User.FindFirst("email")?.Value ??
                               httpContext.User.FindFirst("sub")?.Value ??
                               httpContext.User.Claims.FirstOrDefault(c => c.Type.EndsWith("emailaddress"))?.Value;

                if (string.IsNullOrEmpty(userEmail))
                {
                    return Results.Unauthorized();
                }

                var customerReference = httpContext.User.FindFirst("sub")?.Value ?? userEmail;

                var customer = await maxioService.GetCustomerByReferenceAsync(customerReference);
                if (customer?.Customer == null)
                {
                    return Results.Ok(response);
                }

                var subscriptions = await maxioService.ListSubscriptionsByCustomerAsync(customer.Customer.Id);
                if (subscriptions?.Subscriptions == null || !subscriptions.Subscriptions.Any())
                {
                    return Results.Ok(response);
                }

                response.Subscriptions.AddRange(subscriptions.Subscriptions
                    .Where(s => s.Subscription != null)
                    .Select(s => new MySubscriptionDto
                    {
                        Id = s.Subscription.Id,
                        State = s.Subscription.State,
                        ProductName = s.Subscription.Product?.Name ?? "Unknown",
                        ProductHandle = s.Subscription.Product?.Handle,
                        PricePerCycle = s.Subscription.ProductPriceInCents / 100m,
                        CurrentPeriodEndsAt = s.Subscription.CurrentPeriodEndsAt,
                        NextAssessmentAt = s.Subscription.NextAssessmentAt,
                        ActivatedAt = s.Subscription.ActivatedAt
                    }));

                return Results.Ok(response);
            })
           .Produces<ListMySubscriptionsResponse>()
           .Produces(401)
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }
}

public class ListMySubscriptionsResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public decimal PricePerCycle { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
}
