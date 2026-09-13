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

/// <summary>
/// Subscribe a user to a Maxio plan
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioClient>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioClient maxioClient) =>
            {
                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient maxioClient)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var email = $"{userId}@eshop.local";
        var firstName = userId;
        var lastName = "User";

        var customer = await maxioClient.EnsureCustomerAsync(firstName, lastName, email, userId);

        var subscription = await maxioClient.CreateSubscriptionAsync(request.ProductHandle, customer.Reference);

        var response = new CreateSubscriptionResponse
        {
            Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductHandle = request.ProductHandle,
                ProductName = subscription.Product?.Name ?? request.ProductHandle,
                Price = subscription.ProductPriceInCents / 100m,
                NextBillingDate = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CanceledAt = subscription.CanceledAt
            }
        };

        return Results.Created($"/api/my-subscriptions", response);
    }
}
