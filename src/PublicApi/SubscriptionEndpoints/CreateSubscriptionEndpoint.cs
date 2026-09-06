using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, IMaxioService maxioService, HttpContext httpContext) =>
            {
                var response = new CreateSubscriptionResponse();

                if (string.IsNullOrEmpty(request.ProductHandle))
                {
                    return Results.BadRequest(new { error = "ProductHandle is required" });
                }

                var userEmail = httpContext.User.FindFirst("email")?.Value ??
                               httpContext.User.FindFirst("sub")?.Value ??
                               httpContext.User.Claims.FirstOrDefault(c => c.Type.EndsWith("emailaddress"))?.Value;

                var userName = httpContext.User.FindFirst("name")?.Value ?? "Unknown User";
                var userGivenName = httpContext.User.FindFirst("given_name")?.Value ?? userName;
                var userFamilyName = httpContext.User.FindFirst("family_name")?.Value ?? "";

                if (string.IsNullOrEmpty(userEmail))
                {
                    return Results.Unauthorized();
                }

                var customerReference = httpContext.User.FindFirst("sub")?.Value ?? userEmail;

                var customer = await maxioService.CreateOrGetCustomerAsync(
                    userEmail,
                    userGivenName,
                    userFamilyName,
                    customerReference
                );

                if (customer?.Customer == null)
                {
                    return Results.BadRequest(new { error = "Failed to create or retrieve customer" });
                }

                var subscription = await maxioService.CreateSubscriptionAsync(
                    customer.Customer.Id,
                    request.ProductHandle
                );

                if (subscription?.Subscription == null)
                {
                    return Results.BadRequest(new { error = "Failed to create subscription" });
                }

                response.Success = true;
                response.SubscriptionId = subscription.Subscription.Id;
                response.State = subscription.Subscription.State;
                response.PricePerCycle = subscription.Subscription.ProductPriceInCents / 100m;
                response.NextBillingDate = subscription.Subscription.NextAssessmentAt;
                response.Message = $"Subscription created successfully. Plan renews on {subscription.Subscription.NextAssessmentAt:yyyy-MM-dd}";

                return Results.Ok(response);
            })
           .Produces<CreateSubscriptionResponse>()
           .Produces(400)
           .Produces(401)
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    public Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public bool Success { get; set; }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal PricePerCycle { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public string Message { get; set; } = string.Empty;
}
