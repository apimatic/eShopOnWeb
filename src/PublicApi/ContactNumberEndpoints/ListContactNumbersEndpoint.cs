using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the caller's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IContactNumberService service) =>
            {
                return await HandleAsync(user, service);
            })
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
            ContactNumbers = numbers
                .Select(n => new ContactNumberDto { ContactNumberId = n.Id, PhoneNumber = n.PhoneNumber })
                .ToList()
        };
        return Results.Ok(response);
    }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; init; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; init; }
    public string PhoneNumber { get; init; } = string.Empty;
}
