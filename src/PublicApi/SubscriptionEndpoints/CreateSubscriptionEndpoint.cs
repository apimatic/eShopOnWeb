using System;
using System.Collections.Generic;
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

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionEndpoint.CreateSubscriptionRequest, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext httpContext, IMaxioClient maxioClient) =>
            {
                request.UserId = httpContext.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
                request.Email = httpContext.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
                request.FirstName = httpContext.User.FindFirstValue("given_name") ?? "eShop";
                request.LastName = httpContext.User.FindFirstValue("family_name") ?? "User";
                return await HandleAsync(request, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient maxioClient)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrEmpty(request.UserId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrEmpty(request.Email))
        {
            request.Email = $"{request.UserId}@eshoponweb.local";
        }

        try
        {
            var customer = await maxioClient.GetOrCreateCustomerAsync(request.FirstName, request.LastName, request.Email, request.UserId);
            response.MaxioCustomerId = customer.Id;

            var subscription = await maxioClient.CreateSubscriptionAsync(request.ProductHandle, customer.Id);

            response.Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                State = subscription.State,
                BalanceInCents = subscription.BalanceInCents,
                TotalRevenueInCents = subscription.TotalRevenueInCents,
                ProductPriceInCents = subscription.ProductPriceInCents,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt,
                ActivatedAt = subscription.ActivatedAt,
                CanceledAt = subscription.CanceledAt,
                ExpiresAt = subscription.ExpiresAt,
                CreatedAt = subscription.CreatedAt,
                Currency = subscription.Currency,
                ProductName = subscription.Product?.Name,
                ProductHandle = subscription.Product?.Handle,
                Reference = subscription.Reference
            };

            return Results.Ok(response);
        }
        catch (MaxioException ex)
        {
            return Results.BadRequest(new { error = ex.Message, details = ex.ResponseBody });
        }
    }

    public class CreateSubscriptionRequest : BaseRequest
    {
        public string ProductHandle { get; set; } = string.Empty;

        internal string UserId { get; set; } = string.Empty;
        internal string Email { get; set; } = string.Empty;
        internal string FirstName { get; set; } = string.Empty;
        internal string LastName { get; set; } = string.Empty;
    }
}
