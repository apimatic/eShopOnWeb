using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext context, MaxioSubscriptionService service) =>
            {
                try
                {
                    var userId = context.User.FindFirst("sub")?.Value;
                    if (string.IsNullOrEmpty(userId))
                    {
                        return Results.Unauthorized();
                    }

                    var subscriptions = await service.GetUserSubscriptionsAsync(userId);
                    var response = new ListMySubscriptionsResponse
                    {
                        Subscriptions = subscriptions.Select(s => new MySubscriptionResponse
                        {
                            Id = s.Id,
                            ProductId = s.ProductId,
                            State = s.State,
                            NextBillingDate = s.NextBillingDate,
                            ActivatedAt = s.ActivatedAt,
                            CanceledAt = s.CanceledAt
                        }).ToList()
                    };

                    return Results.Ok(response);
                }
                catch (MaxioException)
                {
                    return Results.StatusCode(StatusCodes.Status502BadGateway);
                }
            })
           .Produces<ListMySubscriptionsResponse>()
           .RequireAuthorization()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }

    public class ListMySubscriptionsResponse
    {
        public List<MySubscriptionResponse> Subscriptions { get; set; } = new();
    }

    public class MySubscriptionResponse
    {
        public long Id { get; set; }
        public long ProductId { get; set; }
        public string State { get; set; } = string.Empty;
        public DateTimeOffset? NextBillingDate { get; set; }
        public DateTimeOffset? ActivatedAt { get; set; }
        public DateTimeOffset? CanceledAt { get; set; }
    }
}
