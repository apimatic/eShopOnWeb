using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// POST /api/contact-numbers — register a mobile number for the signed-in shopper. A number the provider does
/// not consider a usable destination is rejected here (not at send time), and the provider's own canonical
/// form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                RegisterContactNumberRequest request,
                ISmsSender sms,
                IRepository<ContactNumber> repository,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(request.PhoneNumber))
                {
                    return Results.BadRequest(new { message = "A phone number is required." });
                }

                var validation = await sms.ValidateAsync(request.PhoneNumber, ct);
                if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
                {
                    return Results.BadRequest(new { message = "The number is not a usable SMS destination." });
                }

                var canonical = validation.CanonicalNumber;

                // Registering the same number twice is idempotent — return the existing registration.
                var existing = await repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
                var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
                if (already is not null)
                {
                    return Results.Ok(new RegisterContactNumberResponse { ContactNumberId = already.Id, PhoneNumber = already.PhoneNumber });
                }

                var contactNumber = new ContactNumber(buyerId, canonical);
                await repository.AddAsync(contactNumber, ct);

                return Results.Created($"api/contact-numbers/{contactNumber.Id}",
                    new RegisterContactNumberResponse { ContactNumberId = contactNumber.Id, PhoneNumber = contactNumber.PhoneNumber });
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }
}
