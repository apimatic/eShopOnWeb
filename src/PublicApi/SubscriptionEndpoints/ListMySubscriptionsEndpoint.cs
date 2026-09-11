using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Dto;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioOptions _maxioOptions;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMySubscriptionsEndpoint(
        IMaxioClient maxioClient,
        IOptions<MaxioOptions> maxioOptions,
        IHttpContextAccessor httpContextAccessor)
    {
        _maxioClient = maxioClient;
        _maxioOptions = maxioOptions.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async () =>
            {
                return await HandleAsync();
            })
           .Produces<ListMySubscriptionsResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListMySubscriptionsResponse();

        var httpContext = _httpContextAccessor.HttpContext;
        var user = httpContext?.User;
        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.Identity?.Name
            ?? string.Empty;

        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var customerReference = $"eshop-{userId}";
        var customer = await _maxioClient.FindCustomerByReferenceAsync(customerReference);
        if (customer == null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id);

        response.Subscriptions.AddRange(subscriptions.Select(MapToDto));

        return Results.Ok(response);
    }

    private static SubscriptionDto MapToDto(MaxioSubscriptionDto sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            Balance = sub.BalanceInCents / 100m,
            TotalRevenue = sub.TotalRevenueInCents / 100m,
            ProductPrice = sub.ProductPriceInCents / 100m,
            ProductPriceDisplay = $"${sub.ProductPriceInCents / 100m:F2}",
            CurrentPeriodEndsAt = sub.CurrentPeriodEndsAt,
            NextAssessmentAt = sub.NextAssessmentAt,
            ActivatedAt = sub.ActivatedAt,
            CreatedAt = sub.CreatedAt,
            CanceledAt = sub.CanceledAt,
            CancelAtEndOfPeriod = sub.CancelAtEndOfPeriod,
            ProductName = sub.Product?.Name,
            ProductHandle = sub.Product?.Handle,
            CustomerEmail = sub.Customer?.Email,
            Reference = sub.Reference,
            Currency = sub.Currency
        };
    }
}
