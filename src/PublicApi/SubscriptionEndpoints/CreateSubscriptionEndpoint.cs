using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest req, IMaxioService service, UserManager<ApplicationUser> userManager, HttpContext http) =>
        {
            var userName = http.User.Identity?.Name ?? req.UserName ?? "unknown";
            req.UserName = userName;
            var user = await userManager.FindByNameAsync(userName);
            req.Email = user?.Email ?? userName;
            req.FirstName = user?.UserName?.Split('@').FirstOrDefault() ?? "User";
            req.LastName = "Customer";
            return await HandleAsync(req, service);
        })
        .Accepts<CreateSubscriptionRequest>("application/json")
        .Produces<CreateSubscriptionResponse>()
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioService service)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
            return Results.BadRequest(new { error = "ProductHandle is required." });

        // Idempotent customer creation using username/reference
        var customer = await service.EnsureCustomerAsync(
            reference: request.UserName,
            email: request.Email,
            firstName: request.FirstName,
            lastName: request.LastName);

        var subscription = await service.CreateSubscriptionAsync(
            productHandle: request.ProductHandle,
            customerId: customer.Id,
            reference: request.UserName,
            customerReference: request.UserName);

        var response = new CreateSubscriptionResponse
        {
            SubscriptionId = subscription.Id,
            State = subscription.State,
            ProductId = subscription.Product?.Id ?? 0,
            ProductHandle = subscription.Product?.Handle ?? request.ProductHandle,
            ProductName = subscription.Product?.Name ?? "",
            PriceInCents = subscription.ProductPriceInCents,
            NextBillingAt = subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CustomerId = customer.Id,
            CustomerReference = customer.Reference
        };
        return Results.Created($"/api/my-subscriptions", response);
    }
}
