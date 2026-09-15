using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The provider validates it up front; a
/// number it does not consider a usable destination is rejected here, and what is stored is the
/// provider's own canonical form of the number.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, ISmsProvider sms, IRepository<ContactNumber> repository) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (request is null || string.IsNullOrWhiteSpace(request.PhoneNumber))
                    return Results.BadRequest(new { message = "A phone number is required." });

                var lookup = await sms.LookupAsync(request.PhoneNumber);
                if (!lookup.IsUsableDestination || string.IsNullOrEmpty(lookup.CanonicalNumber))
                    return Results.BadRequest(new { message = lookup.Reason ?? "The number is not a usable destination." });

                var canonical = lookup.CanonicalNumber!;

                // Idempotent on the canonical number: registering the same number twice returns the same record.
                var existing = await repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
                var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
                if (already is not null)
                    return Results.Ok(new RegisterContactNumberResponse { ContactNumberId = already.Id, PhoneNumber = already.PhoneNumber });

                var created = await repository.AddAsync(new ContactNumber(buyerId, canonical));
                return Results.Created($"api/contact-numbers/{created.Id}",
                    new RegisterContactNumberResponse { ContactNumberId = created.Id, PhoneNumber = created.PhoneNumber });
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }
}

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number in any form the provider can resolve; it is canonicalised on registration.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    /// <summary>Identifier of the registered number.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form of the number, as stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
