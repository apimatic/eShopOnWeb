using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioBillingService service) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(), service);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioBillingService service)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var reference = user?.FindFirst(ClaimTypes.Name)?.Value ?? user?.Identity?.Name ?? "anonymous";
        var subs = await service.GetSubscriptionsAsync(reference);
        var response = new MySubscriptionsResponse(request.CorrelationId())
        {
            Subscriptions = subs.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                CustomerReference = s.CustomerReference,
                ProductHandle = s.ProductHandle,
                State = s.State,
                NextBillingDate = s.NextBillingDate,
                Price = s.Price
            }).ToList()
        };
        return Results.Ok(response);
    }
}
