using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List subscriptions for the authenticated user
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly MaxioClient _maxioClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(MaxioClient maxioClient, IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<MySubscriptionsResponse>()
            .RequireAuthorization(JwtBearerDefaults.AuthenticationScheme)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return Results.BadRequest(new { error = "No HTTP context available." });

        return await HandleAsync(httpContext);
    }

    private async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var response = new MySubscriptionsResponse();

        var userEmail = httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? httpContext.User.FindFirstValue("unique_name");

        if (string.IsNullOrEmpty(userEmail))
        {
            return Results.BadRequest(new { error = "User identity could not be determined from the token." });
        }

        var customerReference = $"eshop-{userEmail}";
        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference);

        if (customer == null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id);

        response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
        {
            Id = s.Id,
            State = s.State,
            PriceInDollars = s.ProductPriceInCents / 100m,
            Currency = s.Currency,
            NextBillingAt = s.NextAssessmentAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            ActivatedAt = s.ActivatedAt,
            CanceledAt = s.CanceledAt,
            CancelAtEndOfPeriod = s.CancelAtEndOfPeriod,
            CreatedAt = s.CreatedAt,
            ProductName = s.Product?.Name,
            ProductHandle = s.Product?.Handle,
            ProductId = s.Product?.Id
        }));

        return Results.Ok(response);
    }
}
