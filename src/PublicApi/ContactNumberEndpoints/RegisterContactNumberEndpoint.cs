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
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider up front —
/// a number the provider will not deliver to is rejected here, not at send time — and the provider's own
/// canonical form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                RegisterContactNumberRequest request,
                ClaimsPrincipal user,
                ISmsGateway gateway,
                IRepository<ContactNumber> repository,
                CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(request?.PhoneNumber))
                    return Results.BadRequest(new { message = "A phone number is required." });

                PhoneValidationResult validation;
                try
                {
                    validation = await gateway.ValidateNumberAsync(request.PhoneNumber, ct);
                }
                catch (SmsGatewayException ex)
                {
                    // Could not reach the provider to validate — this is an outage, not a bad number.
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
                }

                if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
                    return Results.BadRequest(new { message = "That number is not a usable SMS destination." });

                var canonical = validation.CanonicalNumber;

                // If the shopper already has this number on file, return the existing one rather than duplicating.
                var existing = await repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
                var already = existing.Find(c => c.PhoneNumber == canonical);
                if (already != null)
                    return Results.Ok(new RegisterContactNumberResponse(already.Id, already.PhoneNumber));

                var contactNumber = new ContactNumber(buyerId, canonical);
                contactNumber = await repository.AddAsync(contactNumber, ct);

                var response = new RegisterContactNumberResponse(contactNumber.Id, contactNumber.PhoneNumber);
                return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }
}

public record RegisterContactNumberRequest(string PhoneNumber);

/// <summary>The created number. <c>contactNumberId</c> is the identifier the other endpoints act on.</summary>
public record RegisterContactNumberResponse(int ContactNumberId, string PhoneNumber);
