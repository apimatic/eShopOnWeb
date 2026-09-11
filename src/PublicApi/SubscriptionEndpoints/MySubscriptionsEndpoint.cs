using System;
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
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioClient maxioClient) =>
            {
                return await HandleAsync(new MySubscriptionsRequest(), maxioClient);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioClient maxioClient)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
            return Results.Unauthorized();

        var userId = user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userId))
            return Results.Unauthorized();

        var response = new MySubscriptionsResponse(request.CorrelationId());

        var reference = $"eshop-{userId}";
        var customer = await maxioClient.FindCustomerByReferenceAsync(reference);
        if (customer == null)
            return Results.Ok(response);

        var subscriptions = await maxioClient.ListCustomerSubscriptionsAsync(customer.Id);

        response.Subscriptions = subscriptions
            .Select(sub => new SubscriptionDto
            {
                Id = sub.Id,
                State = sub.State,
                PlanName = sub.Product?.Name ?? string.Empty,
                PlanHandle = sub.Product?.Handle ?? string.Empty,
                Price = (sub.Product?.PriceInCents ?? 0) / 100m,
                NextBillingDate = sub.NextAssessmentAt,
                ActivatedAt = sub.ActivatedAt,
                CreatedAt = sub.CreatedAt,
                CanceledAt = sub.CanceledAt,
                PaymentCollectionMethod = sub.PaymentCollectionMethod
            })
            .ToList();

        return Results.Ok(response);
    }
}
