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
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public record ContactNumberDto(int ContactNumberId, string PhoneNumber, DateTimeOffset RegisteredDate);
public record ListContactNumbersResponse(IReadOnlyList<ContactNumberDto> ContactNumbers);

/// <summary>The signed-in shopper's own registered numbers — never another shopper's.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IContactNumberService service, CancellationToken ct) =>
            {
                var callerId = user.GetCallerId();
                if (callerId is null)
                {
                    return Results.Unauthorized();
                }

                var numbers = await service.ListAsync(callerId, ct);
                var dtos = numbers
                    .Select(n => new ContactNumberDto(n.Id, n.E164Number, n.RegisteredDate))
                    .ToList();
                return Results.Ok(new ListContactNumbersResponse(dtos));
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    // Not used: this endpoint has no request payload and resolves everything in AddRoute.
    public Task<IResult> HandleAsync(IContactNumberService service) => Task.FromResult(Results.Ok());
}
