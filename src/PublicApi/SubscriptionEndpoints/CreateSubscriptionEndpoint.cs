using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioApiService, IHttpContextAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, IMaxioApiService maxioApi, IHttpContextAccessor httpContextAccessor) =>
            {
                return await HandleAsync(request, maxioApi, httpContextAccessor);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>(200)
            .Produces<CreateSubscriptionResponse>(400)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioApiService maxioApi, IHttpContextAccessor httpContextAccessor)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());
        var httpContext = httpContextAccessor.HttpContext;

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            response.Success = false;
            response.ErrorMessage = "Plan handle is required";
            return Results.BadRequest(response);
        }

        var user = httpContext!.User;
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            response.Success = false;
            response.ErrorMessage = "User not authenticated";
            return Results.Unauthorized();
        }

        var email = user.FindFirst(ClaimTypes.Email)?.Value ?? $"{userId}@eshop.local";
        var firstName = user.FindFirst(ClaimTypes.GivenName)?.Value ?? "User";
        var lastName = user.FindFirst(ClaimTypes.Surname)?.Value ?? userId;

        var plan = await maxioApi.GetProductByHandleAsync(request.PlanHandle);
        if (plan == null)
        {
            response.Success = false;
            response.ErrorMessage = "Plan not found";
            return Results.BadRequest(response);
        }

        var customer = await maxioApi.GetOrCreateCustomerAsync(userId, firstName, lastName, email);
        if (customer == null)
        {
            response.Success = false;
            response.ErrorMessage = "Failed to create or retrieve customer";
            return Results.BadRequest(response);
        }

        var subscription = await maxioApi.CreateSubscriptionAsync(customer.Id, plan.Id, plan.Handle ?? "");
        if (subscription == null)
        {
            response.Success = false;
            response.ErrorMessage = "Failed to create subscription";
            return Results.BadRequest(response);
        }

        response.Success = true;
        response.SubscriptionId = subscription.Id;
        response.State = subscription.State;
        response.CustomerMaxioId = customer.Id;
        response.PlanName = plan.Name;
        response.PricePerMonth = plan.PriceInCents / 100m;
        response.NextBillingAt = subscription.NextBillingAt;
        response.Message = $"Successfully subscribed to {plan.Name}";

        return Results.Ok(response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
    public int SubscriptionId { get; set; }
    public string? State { get; set; }
    public int CustomerMaxioId { get; set; }
    public string? PlanName { get; set; }
    public decimal PricePerMonth { get; set; }
    public DateTime? NextBillingAt { get; set; }
}
