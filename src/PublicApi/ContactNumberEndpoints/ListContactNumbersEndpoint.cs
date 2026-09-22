using System;
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

/// <summary>Lists the signed-in shopper's registered mobile numbers.</summary>
public class ListContactNumbersEndpoint
    : IEndpoint<IResult, ListContactNumbersRequest, IShopperContactService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IShopperContactService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ListContactNumbersRequest { BuyerId = user.Identity?.Name },
                    service, ct);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IShopperContactService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await service.ListAsync(request.BuyerId!, ct);
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers
                .Select(n => new ContactNumberDto(n.Id, n.PhoneNumber, n.RegisteredAt))
                .ToList()
        };
        return Results.Ok(response);
    }
}

public class ListContactNumbersRequest : BaseRequest
{
    public string? BuyerId { get; set; }
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public sealed record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset RegisteredAt);
