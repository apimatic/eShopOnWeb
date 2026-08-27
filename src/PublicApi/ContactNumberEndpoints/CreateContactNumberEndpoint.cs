using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with
/// the provider up front and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IPhoneNumberLookupService _lookupService;

    public CreateContactNumberEndpoint(
        IRepository<ContactNumber> contactNumberRepository,
        IPhoneNumberLookupService lookupService)
    {
        _contactNumberRepository = contactNumberRepository;
        _lookupService = lookupService;
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
        var ownerId = user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        PhoneNumberLookupResult lookup;
        try
        {
            lookup = await _lookupService.LookupAsync(request.PhoneNumber);
        }
        catch (MessageProviderException)
        {
            return Results.Problem("Phone number validation is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!lookup.IsValid || string.IsNullOrEmpty(lookup.CanonicalNumber))
        {
            return Results.BadRequest(new { message = "The phone number is not a usable destination." });
        }

        var existing = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndNumberSpecification(ownerId, lookup.CanonicalNumber));
        if (existing != null)
        {
            throw new DuplicateException("This phone number is already registered.");
        }

        var contactNumber = await _contactNumberRepository.AddAsync(new ContactNumber(ownerId, lookup.CanonicalNumber));

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
