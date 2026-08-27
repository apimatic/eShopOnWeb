using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// with the messaging provider and stored in the provider's canonical form.
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
            .Produces<CreateContactNumberResponse>()
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
            return Results.BadRequest(new { error = "A phone number is required." });
        }

        var lookup = await _phoneNumberLookup.LookupAsync(request.PhoneNumber);
        if (!lookup.IsValid || lookup.CanonicalNumber is null)
        {
            return Results.BadRequest(new { error = lookup.Error ?? "The phone number is not a usable destination." });
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        if (existing.Any(c => c.PhoneNumber == lookup.CanonicalNumber))
        {
            return Results.Conflict(new { error = "This number is already registered." });
        }

        var contactNumber = await _contactNumberRepository.AddAsync(new ContactNumber(buyerId, lookup.CanonicalNumber));

        return Results.Created($"api/contact-numbers/{contactNumber.Id}", new CreateContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedUtc = contactNumber.CreatedUtc
        });
    }
}

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}
