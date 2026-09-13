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

/// <summary>
/// List subscriptions for the authenticated user
/// </summary>
public class ListMySubscriptionsEndpoint : IEndpoint<IResult, IMaxioClient>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioClient maxioClient) =>
            {
                return await HandleAsync(maxioClient);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioClient maxioClient)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var userId = httpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var subscriptions = await maxioClient.ListSubscriptionsByCustomerReferenceAsync(userId);

        var dtos = subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            ProductHandle = s.Product?.Handle ?? string.Empty,
            ProductName = s.Product?.Name ?? string.Empty,
            Price = s.ProductPriceInCents / 100m,
            NextBillingDate = s.NextAssessmentAt,
            ActivatedAt = s.ActivatedAt,
            CanceledAt = s.CanceledAt
        }).ToList();

        var response = new ListMySubscriptionsResponse { Subscriptions = dtos };
        return Results.Ok(response);
    }
}
