using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>The signed-in shopper's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IContactNumberService service, CancellationToken ct) =>
            {
                var caller = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(caller))
                {
                    return Results.Unauthorized();
                }

                var numbers = await service.ListAsync(caller, ct);
                var response = new ListContactNumbersResponse
                {
                    ContactNumbers = numbers
                        .Select(n => new ContactNumberDto(n.Id, n.E164Number, n.RegisteredAt))
                        .ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(IContactNumberService service) => Task.FromResult<IResult>(Results.Empty);
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public record ContactNumberDto(int ContactNumberId, string E164Number, DateTimeOffset RegisteredAt);
