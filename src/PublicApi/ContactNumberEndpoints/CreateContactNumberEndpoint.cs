using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// provider up front and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IPhoneNumberValidator _phoneNumberValidator;

    public CreateContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository,
        IPhoneNumberValidator phoneNumberValidator)
    {
        _contactNumberRepository = contactNumberRepository;
        _phoneNumberValidator = phoneNumberValidator;
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
        var buyerId = user.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var response = new CreateContactNumberResponse(request.CorrelationId());

        var canonicalNumber = await _phoneNumberValidator.ValidateAndNormalizeAsync(request.PhoneNumber, request.CountryCode);
        if (canonicalNumber is null)
        {
            response.Message = "The number is not a usable destination according to the messaging provider.";
            return Results.BadRequest(response);
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        if (existing.Any(c => c.PhoneNumber == canonicalNumber))
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contactNumber = await _contactNumberRepository.AddAsync(new ContactNumber(buyerId, canonicalNumber));

        response.ContactNumberId = contactNumber.Id;
        response.ContactNumber = ContactNumberDto.FromEntity(contactNumber);
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
