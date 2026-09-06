using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint
{
    public static void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            HandleAsync)
           .Produces<CreateSubscriptionResponse>()
           .WithTags("SubscriptionEndpoints")
           .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        IMaxioBillingService billingService,
        IRepository<MaxioSubscription> subscriptionRepository,
        HttpContext httpContext)
    {
        var response = new CreateSubscriptionResponse();

        try
        {
            var user = httpContext.User;
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var email = user.FindFirst(ClaimTypes.Email)?.Value ?? "unknown@example.com";
            var name = user.FindFirst(ClaimTypes.Name)?.Value ?? "User";
            var nameParts = name.Split(' ', 2);
            var firstName = nameParts[0];
            var lastName = nameParts.Length > 1 ? nameParts[1] : "";

            var customer = await billingService.GetOrCreateCustomerAsync(
                userId, firstName, lastName, email);

            var maxioSub = await billingService.CreateSubscriptionAsync(
                customer.Id, request.ProductHandle);

            var dbSub = new MaxioSubscription
            {
                UserId = userId,
                MaxioCustomerId = customer.Id,
                MaxioSubscriptionId = maxioSub.Id,
                ProductHandle = request.ProductHandle,
                State = maxioSub.State,
                CurrentPriceInCents = maxioSub.CurrentPriceInCents,
                ActivatedAt = maxioSub.ActivatedAt,
                NextBillingAt = maxioSub.NextBillingAt,
                CreatedAt = maxioSub.CreatedAt,
                UpdatedAt = maxioSub.UpdatedAt
            };

            await subscriptionRepository.AddAsync(dbSub);

            response.Success = true;
            response.SubscriptionId = maxioSub.Id;
            response.State = maxioSub.State;
            response.NextBillingAt = maxioSub.NextBillingAt;
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Error = ex.Message;
            return Results.BadRequest(response);
        }

        return Results.Created($"/api/subscriptions/{response.SubscriptionId}", response);
    }
}
