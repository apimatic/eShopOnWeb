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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not
/// consider a usable destination is rejected here; what is stored is the provider's own
/// canonical E.164 form, not whatever the caller typed.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                RegisterContactNumberRequest request,
                ClaimsPrincipal user,
                ISmsMessagingService sms,
                IRepository<ContactNumber> repository) =>
            {
                var ownerId = user.GetOwnerId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                if (request is null || string.IsNullOrWhiteSpace(request.PhoneNumber))
                {
                    return Results.BadRequest(new { message = "A phoneNumber is required." });
                }

                // Reject an unusable destination now, not when a message later fails to go out.
                var validation = await sms.ValidateNumberAsync(request.PhoneNumber);
                if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
                {
                    return Results.BadRequest(new { message = "The phone number is not a usable destination and was not registered." });
                }

                var canonical = validation.CanonicalNumber;

                // Avoid registering the same number twice for the same shopper.
                var existing = await repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
                var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
                if (duplicate is not null)
                {
                    return Results.Ok(new RegisterContactNumberResponse
                    {
                        ContactNumberId = duplicate.Id,
                        PhoneNumber = duplicate.PhoneNumber
                    });
                }

                var contact = new ContactNumber(ownerId, canonical);
                await repository.AddAsync(contact);

                return Results.Created($"api/contact-numbers/{contact.Id}", new RegisterContactNumberResponse
                {
                    ContactNumberId = contact.Id,
                    PhoneNumber = contact.PhoneNumber
                });
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }
}

public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    /// <summary>Identifier of the registered number (top-level, so the flow can be driven end to end).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The canonical E.164 number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
