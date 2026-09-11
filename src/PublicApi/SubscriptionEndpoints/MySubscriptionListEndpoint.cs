using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListEndpoint : IEndpoint<IResult, MySubscriptionListRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (ClaimsPrincipal user, IMaxioBillingService svc) =>
            {
                var req = new MySubscriptionListRequest();
                req.UserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub") ?? user.Identity?.Name ?? string.Empty;
                return await HandleAsync(req, svc);
            })
            .Produces<MySubscriptionListResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionListRequest request, IMaxioBillingService svc)
    {
        var subs = await svc.ListSubscriptionsAsync(request.UserId);
        var response = new MySubscriptionListResponse(request.CorrelationId())
        {
            Subscriptions = new List<MySubscriptionDto>()
        };
        foreach (var s in subs)
        {
            response.Subscriptions.Add(new MySubscriptionDto
            {
                Id = s.Id,
                ProductHandle = s.ProductHandle,
                State = s.State,
                NextBillingAt = s.NextBillingAt,
                PriceInCents = (long)s.PriceInCents,
                Price = (s.PriceInCents / 100m).ToString("F2")
            });
        }
        return Results.Ok(response);
    }
}

public class MySubscriptionListRequest : BaseRequest
{
    public string UserId { get; set; } = string.Empty;
}

public class MySubscriptionListResponse : BaseResponse
{
    public MySubscriptionListResponse(Guid correlationId) : base(correlationId) { }
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string NextBillingAt { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Price { get; set; } = string.Empty;
}
