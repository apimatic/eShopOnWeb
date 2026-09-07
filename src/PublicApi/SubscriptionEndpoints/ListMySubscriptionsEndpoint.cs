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
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioService maxioService, HttpContext context) =>
            {
                var endpoint = new ListMySubscriptionsEndpoint();
                return await endpoint.HandleAsync(new EmptyRequest(), maxioService, context.User);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IMaxioService maxioService)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(EmptyRequest request, IMaxioService maxioService, ClaimsPrincipal user)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = user.FindFirst(ClaimTypes.Email)?.Value;
        var firstName = user.FindFirst("FirstName")?.Value ?? "Customer";
        var lastName = user.FindFirst("LastName")?.Value ?? "";

        if (string.IsNullOrEmpty(userId))
        {
            return Results.BadRequest(new { error = "User identity not found" });
        }

        try
        {
            var customer = await maxioService.GetOrCreateCustomerAsync(userId, firstName, lastName, email ?? "");

            var subscriptions = await maxioService.ListCustomerSubscriptionsAsync(customer.Id.ToString());

            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionResponse
            {
                Id = s.Id,
                CustomerId = s.CustomerId,
                ProductHandle = s.ProductHandle,
                ProductName = s.ProductName,
                State = s.State,
                ActivatedAt = s.ActivatedAt,
                NextBillingAt = s.NextBillingAt,
                CurrentPeriodAmountInCents = s.CurrentPeriodAmountInCents,
                CurrentPeriodAmountFormatted = s.CurrentPeriodAmountInCents.HasValue
                    ? $"${s.CurrentPeriodAmountInCents / 100m:F2}"
                    : ""
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionResponse> Subscriptions { get; } = new();
}
