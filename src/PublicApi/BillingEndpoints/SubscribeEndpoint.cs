using System.Security.Claims;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Subscriptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BillingEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan, without card details (the plan is invoiced)
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, PlanHandleRequest, ClaimsPrincipal, BillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlanHandleRequest request, ClaimsPrincipal user, BillingService billing) =>
            {
                return await HandleAsync(request, user, billing);
            })
            .Produces<SubscriptionDto>(StatusCodes.Status201Created)
            .WithTags("BillingEndpoints");
    }

    public Task<IResult> HandleAsync(PlanHandleRequest request, ClaimsPrincipal user, BillingService billing)
    {
        return BillingException.RunAsync(async () =>
        {
            var userName = BillingService.GetUserName(user);
            var plan = await billing.GetPlanByHandleAsync(request?.ResolvedHandle);
            var customer = await billing.EnsureCustomerAsync(userName);

            // Without card details the plan can only be invoiced, so collection is by remittance.
            var created = await billing.Client.Subscriptions.CreateSubscription(new CreateSubscriptionOperationRequest
            {
                Body = new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductId = plan.Id,
                        CustomerId = customer.Id,
                        PaymentCollectionMethod = CollectionMethod.Remittance,
                    },
                },
            });

            var dto = BillingService.ToDto(created.Subscription!);
            return Results.Created($"api/subscriptions/{dto.Id}", dto);
        });
    }
}
