using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// with the provider up front and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal, IRepository<ContactNumber>>
{
    private readonly IPhoneNumberLookup _phoneNumberLookup;

    public CreateContactNumberEndpoint(IPhoneNumberLookup phoneNumberLookup)
    {
        _phoneNumberLookup = phoneNumberLookup;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository) =>
            {
                return await HandleAsync(request, user, contactNumberRepository);
            })
            .Produces<CreateContactNumberResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository)
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

        // Reject unusable destinations now, not when a message fails to go out.
        var lookup = await _phoneNumberLookup.LookupAsync(request.PhoneNumber);
        if (!lookup.IsValid || lookup.CanonicalNumber is null)
        {
            return Results.BadRequest(new
            {
                message = "The phone number is not a usable destination.",
                validationErrors = lookup.ValidationErrors
            });
        }

        var existing = await contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        if (existing.Any(c => c.PhoneNumber == lookup.CanonicalNumber))
        {
            return Results.Conflict(new { message = "This number is already registered." });
        }

        // Store the provider's canonical form, not what the caller typed.
        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalNumber);
        contactNumber = await contactNumberRepository.AddAsync(contactNumber);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
