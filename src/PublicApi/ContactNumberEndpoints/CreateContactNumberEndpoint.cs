using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with
/// the provider up front and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IPhoneNumberLookup _phoneNumberLookup;

    public CreateContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository, IPhoneNumberLookup phoneNumberLookup)
    {
        _contactNumberRepository = contactNumberRepository;
        _phoneNumberLookup = phoneNumberLookup;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, ClaimsPrincipal user)
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

        // Reject unusable destinations now, not when a message later fails to go out.
        var lookup = await _phoneNumberLookup.LookupAsync(request.PhoneNumber.Trim());
        if (!lookup.IsValid || lookup.CanonicalNumber is null)
        {
            return Results.BadRequest(new { message = $"The provider does not consider this a usable destination. {lookup.ValidationError}".Trim() });
        }

        // Store the provider's canonical form, not whatever the caller typed.
        var contactNumber = new ContactNumber(buyerId, lookup.CanonicalNumber);
        contactNumber = await _contactNumberRepository.AddAsync(contactNumber);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedUtc = contactNumber.CreatedUtc
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
