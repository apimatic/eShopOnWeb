using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset RegisteredAt);

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>Lists the caller's own registered numbers (only ever the caller's own).</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, HttpContext http) => await HandleAsync(http))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var ownerId = CallerIdentity.GetOwnerId(http.User);
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var repository = http.RequestServices.GetRequiredService<IReadRepository<ContactNumber>>();
        var numbers = await repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), http.RequestAborted);

        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers
                .Select(n => new ContactNumberDto(n.Id, n.PhoneNumber, n.RegisteredAt))
                .ToList()
        };
        return Results.Ok(response);
    }
}
