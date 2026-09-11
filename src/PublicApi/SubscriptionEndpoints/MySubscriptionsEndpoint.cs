using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, IMaxioService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (IMaxioService service, UserManager<ApplicationUser> userManager, HttpContext http) =>
        {
            var userName = http.User.Identity?.Name ?? "unknown";
            return await HandleAsync(new MySubscriptionsRequest { UserName = userName }, service);
        })
        .Produces<List<MySubscriptionResponse>>()
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, IMaxioService service)
    {
        // Find customer by reference (username)
        var customer = await service.FindCustomerByReferenceAsync(request.UserName);
        if (customer == null)
            return Results.Ok(new List<MySubscriptionResponse>());

        var subs = await service.ListSubscriptionsForCustomerAsync(customer.Id);
        var response = subs.Select(s => new MySubscriptionResponse
        {
            Id = s.Id,
            State = s.State,
            ProductId = s.Product?.Id ?? 0,
            ProductHandle = s.Product?.Handle ?? "",
            ProductName = s.Product?.Name ?? "",
            PriceInCents = s.ProductPriceInCents,
            NextBillingAt = s.NextAssessmentAt,
            CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
            CustomerId = customer.Id,
            CustomerReference = customer.Reference
        }).ToList();
        return Results.Ok(response);
    }
}

public class MySubscriptionsRequest : BaseRequest
{
    public string UserName { get; set; } = string.Empty;
}
