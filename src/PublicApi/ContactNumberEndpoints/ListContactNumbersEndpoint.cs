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

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Returns the caller's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IContactNumberService service) =>
                await HandleAsync(user, service))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IContactNumberService service)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await service.ListAsync(buyerId);
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(c => new ContactNumberDto
            {
                ContactNumberId = c.Id,
                PhoneNumber = c.PhoneNumber,
                NationalFormat = c.NationalFormat,
                RegisteredAt = c.RegisteredAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}
