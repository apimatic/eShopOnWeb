using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>Lists the signed-in shopper's own registered numbers — never another shopper's.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, IOrderNotificationService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service, ClaimsPrincipal user) =>
                await HandleAsync(service, user))
            .Produces<ContactNumbersResponse>()
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderNotificationService service, ClaimsPrincipal user)
    {
        var buyerId = CallerIdentity.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await service.GetContactNumbersAsync(buyerId);
        var response = new ContactNumbersResponse
        {
            ContactNumbers = numbers
                .Select(n => new ContactNumberDto { ContactNumberId = n.Id, PhoneNumber = n.PhoneNumber, RegisteredAt = n.RegisteredAt })
                .ToList()
        };
        return Results.Ok(response);
    }
}
