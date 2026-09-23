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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public record ContactNumberDto(int ContactNumberId, string PhoneNumber);
public record ListContactNumbersResponse(IReadOnlyList<ContactNumberDto> ContactNumbers);

/// <summary>The signed-in shopper's registered numbers (only their own).</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, string?, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(user.FindFirstValue(ClaimTypes.Name), service);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(string? ownerId, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var numbers = await service.ListAsync(ownerId, default);
        var dtos = numbers.Select(n => new ContactNumberDto(n.Id, n.PhoneNumber)).ToList();
        return Results.Ok(new ListContactNumbersResponse(dtos));
    }
}
