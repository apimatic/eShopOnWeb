using System;
using System.Collections.Generic;
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

public class SubscriptionListEndpoint : IEndpoint<IResult, object, IMaxioClient, UserManager<ApplicationUser>>
{
    private readonly IHttpContextAccessor _http;
    public SubscriptionListEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (IMaxioClient maxio, UserManager<ApplicationUser> userManager) =>
        {
            return await HandleAsync(new {}, maxio, userManager);
        })
        .Produces<ListSubscriptionResponse>()
        .RequireAuthorization()
        .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(object request, IMaxioClient maxio, UserManager<ApplicationUser> userManager)
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
            return Results.Ok(new ListSubscriptionResponse(Guid.NewGuid()) { Subscriptions = new() });
        }
        var subs = await maxio.ListSubscriptionsAsync(customer.Id);
        var response = new ListSubscriptionResponse(Guid.NewGuid())
        {
            Subscriptions = subs.Select(s => new SubscriptionDto
            {
                SubscriptionId = s.Id,
                State = s.State,
                ProductId = s.ProductId,
                CustomerId = s.CustomerId,
                NextBillingAt = s.NextBillingAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListSubscriptionResponse : BaseResponse
{
    public ListSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = "";
    public int ProductId { get; set; }
    public int CustomerId { get; set; }
    public string NextBillingAt { get; set; } = "";
}
