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

/// <summary>Lists the caller's own registered contact numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, IContactNumberService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(service, user);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(IContactNumberService service, ClaimsPrincipal user)
    {
        var ownerId = user.GetUserName();
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var numbers = await service.ListAsync(ownerId);
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers
                .Select(n => new ContactNumberDto(n.Id, n.PhoneNumber, n.CreatedAt))
                .ToList()
        };
        return Results.Ok(response);
    }
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public record ContactNumberDto(int ContactNumberId, string PhoneNumber, System.DateTimeOffset CreatedAt);
