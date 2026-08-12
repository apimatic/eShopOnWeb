using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>Lists the caller's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    private readonly IContactNumberService _contactNumberService;

    public ListContactNumbersEndpoint(IContactNumberService contactNumberService)
    {
        _contactNumberService = contactNumberService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct) => await HandleAsync(user, ct))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var ownerId = user.GetUsername();
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var numbers = await _contactNumberService.ListAsync(ownerId, ct);

        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.PhoneNumber,
                RegisteredAt = n.RegisteredAt
            }).ToList()
        };

        return Results.Ok(response);
    }
}
