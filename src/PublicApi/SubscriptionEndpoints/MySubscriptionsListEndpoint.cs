using System;
using System.Collections.Generic;
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

public class MySubscriptionsListEndpoint : IEndpoint<IResult, MaxioApiClient>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsListEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (MaxioApiClient maxioClient) =>
            {
                return await HandleAsync(maxioClient);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioApiClient maxioClient)
    {
        var response = new ListMySubscriptionsResponse();

        var httpContext = _httpContextAccessor.HttpContext;
        var userName = httpContext?.User?.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var customerRef = $"eshop-{userName}";
        var existingCustomer = await maxioClient.FindCustomerByReferenceAsync(customerRef);

        if (existingCustomer?.Customer == null)
        {
            return Results.Ok(response);
        }

        var subscriptions = await maxioClient.ListCustomerSubscriptionsAsync(
            existingCustomer.Customer.Id);

        if (subscriptions != null)
        {
            response.Subscriptions = subscriptions
                .Select(s => new SubscriptionDto
                {
                    Id = s.Id,
                    State = s.State,
                    Balance_in_cents = s.Balance_in_cents,
                    Total_revenue_in_cents = s.Total_revenue_in_cents,
                    Product_price_in_cents = s.Product_price_in_cents,
                    Current_period_ends_at = s.Current_period_ends_at,
                    Next_assessment_at = s.Next_assessment_at,
                    Activated_at = s.Activated_at,
                    Created_at = s.Created_at,
                    Cancel_at_end_of_period = s.Cancel_at_end_of_period,
                    Canceled_at = s.Canceled_at,
                    Product = s.Product,
                    Customer = s.Customer
                })
                .ToList();
        }

        return Results.Ok(response);
    }
}
