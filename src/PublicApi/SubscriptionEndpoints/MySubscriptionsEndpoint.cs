using System;
using System.Security.Claims;
using System.Threading.Tasks;
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
            async (HttpContext http, IMaxioClient client) =>
            {
                var req = new MySubscriptionsRequest();
                req.UserId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0";
                return await HandleAsync(req, client);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioClient client)
    {
        var response = new MySubscriptionsResponse();
        var userReference = $"eshop-{request.UserId}";
        var customer = await client.LookupCustomerByReferenceAsync(userReference);
        if (customer != null)
        {
            var subs = await client.GetCustomerSubscriptionsAsync(customer.Id);
            foreach (var s in subs)
            {
                response.Subscriptions.Add(new SubscriptionDto
                {
                    Id = s.Id,
                    PlanHandle = s.ProductHandle ?? "",
                    State = s.State,
                    NextBillingAt = s.NextBillingAt,
                    Reference = s.Reference
                });
            }
        }
        return Results.Ok(response);
    }
}

public class MySubscriptionsRequest : BaseRequest
{
    public string UserId { get; set; } = "0";
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse() { }
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public System.Collections.Generic.List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = "";
    public string State { get; set; } = "";
    public string NextBillingAt { get; set; } = "";
    public string Reference { get; set; } = "";
}
