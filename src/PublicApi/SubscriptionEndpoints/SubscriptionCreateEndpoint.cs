using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioClient, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _http;
    public SubscriptionCreateEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (CreateSubscriptionRequest req, IMaxioClient maxio, UserManager<ApplicationUser> userManager) =>
        {
            return await HandleAsync(req, maxio, userManager);
        })
        .Produces<CreateSubscriptionResponse>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient maxio, UserManager<ApplicationUser> userManager)
    {
        var context = _http.HttpContext;
        var userName = context?.User.FindFirstValue(ClaimTypes.Name) ?? context?.User.Identity?.Name ?? "";
        if (string.IsNullOrEmpty(userName)) return Results.Unauthorized();
        var user = await userManager.FindByNameAsync(userName);
        if (user == null) return Results.Unauthorized();

        var reference = user.Id.ToString();
        var customer = await maxio.LookupCustomerAsync(reference);
        if (customer == null)
        {
            customer = await maxio.CreateCustomerAsync(reference, user.Email ?? userName, "", "");
        }

        // Resolve product by handle if needed; here assume planId passed in request is Maxio product handle or id
        var productId = request.PlanId;
        if (string.IsNullOrEmpty(request.PlanHandle) == false)
        {
            var plans = await maxio.ListPlansAsync();
            var plan = plans.FirstOrDefault(p => p.Handle == request.PlanHandle);
            if (plan == null) return Results.BadRequest(new { error = "Plan handle not found" });
            productId = plan.Id;
        }

        var sub = await maxio.CreateSubscriptionAsync(customer.Id, productId);
        return Results.Ok(new CreateSubscriptionResponse(Guid.NewGuid())
        {
            SubscriptionId = sub.Id,
            State = sub.State,
            PlanName = request.PlanHandle ?? productId.ToString(),
            NextBillingAt = sub.NextBillingAt,
            CustomerId = sub.CustomerId
        });
    }
}

public class CreateSubscriptionRequest
{
    public int PlanId { get; set; }
    public string? PlanHandle { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public string PlanName { get; set; } = "";
    public string NextBillingAt { get; set; } = "";
    public int CustomerId { get; set; }
}
