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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, IMaxioService maxioService, HttpContext context) =>
            {
                var endpoint = new CreateSubscriptionEndpoint();
                return await endpoint.HandleAsync(request, maxioService, context.User);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService)
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService maxioService, ClaimsPrincipal user)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = user.FindFirst(ClaimTypes.Email)?.Value;
        var firstName = user.FindFirst("FirstName")?.Value ?? "Customer";
        var lastName = user.FindFirst("LastName")?.Value ?? "";

        if (string.IsNullOrEmpty(userId))
        {
            return Results.BadRequest(new { error = "User identity not found" });
        }

        if (string.IsNullOrEmpty(request.ProductHandle))
        {
            return Results.BadRequest(new { error = "ProductHandle is required" });
        }

        try
        {
            var customer = await maxioService.GetOrCreateCustomerAsync(userId, firstName, lastName, email ?? "");

            var subscription = await maxioService.CreateSubscriptionAsync(customer.Id.ToString(), request.ProductHandle);

            response.Subscription = new SubscriptionResponse
            {
                Id = subscription.Id,
                CustomerId = subscription.CustomerId,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.ProductName,
                State = subscription.State,
                ActivatedAt = subscription.ActivatedAt,
                NextBillingAt = subscription.NextBillingAt,
                CurrentPeriodAmountInCents = subscription.CurrentPeriodAmountInCents,
                CurrentPeriodAmountFormatted = subscription.CurrentPeriodAmountInCents.HasValue
                    ? $"${subscription.CurrentPeriodAmountInCents / 100m:F2}"
                    : ""
            };

            return Results.Created($"/api/subscriptions/{subscription.Id}", response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = "";
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionResponse? Subscription { get; set; }
}

public class SubscriptionResponse
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string State { get; set; } = "";
    public DateTime? ActivatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public decimal? CurrentPeriodAmountInCents { get; set; }
    public string CurrentPeriodAmountFormatted { get; set; } = "";
}
