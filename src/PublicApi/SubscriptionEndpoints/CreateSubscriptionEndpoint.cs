using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (HttpContext http, IMaxioClient client, CreateSubscriptionRequest req) =>
            {
                req.UserEmail = http.User.FindFirst(ClaimTypes.Email)?.Value ?? http.User.FindFirst("email")?.Value ?? http.User.Identity?.Name ?? "unknown";
                req.UserId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0";
                return await HandleAsync(req, client);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient client)
    {
        var response = new CreateSubscriptionResponse();
        try
        {
            var userReference = $"eshop-{request.UserId}";
            var customer = await client.LookupCustomerByReferenceAsync(userReference);
            if (customer == null)
            {
                customer = await client.CreateCustomerAsync(userReference, request.UserEmail, "Shopper", "User");
            }

            var subReference = $"{userReference}:{request.PlanHandle}";
            var existing = await client.LookupSubscriptionByReferenceAsync(subReference);
            if (existing != null)
            {
                response.SubscriptionId = existing.Id;
                response.PlanHandle = existing.ProductHandle ?? request.PlanHandle;
                response.State = existing.State;
                response.NextBillingAt = existing.NextBillingAt;
                response.Message = "Subscription already exists.";
                return Results.Ok(response);
            }

            var sub = await client.CreateSubscriptionAsync(request.PlanHandle, userReference, subReference, customer.Id);
            response.SubscriptionId = sub.Id;
            response.PlanHandle = sub.ProductHandle ?? request.PlanHandle;
            response.State = sub.State;
            response.NextBillingAt = sub.NextBillingAt;
            response.Message = "Subscribed successfully.";
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = ex.Message;
            return Results.BadRequest(response);
        }
        return Results.Ok(response);
    }
}

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string UserId { get; set; } = "";
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = "";
    public string State { get; set; } = "";
    public string NextBillingAt { get; set; } = "";
    public bool Success { get; set; } = true;
    public string Message { get; set; } = "";
}
