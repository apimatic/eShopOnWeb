using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// by the messaging provider and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsProviderClient _smsProviderClient;

    public CreateContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository, ISmsProviderClient smsProviderClient)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsProviderClient = smsProviderClient;
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
        var response = new CreateContactNumberResponse(request.CorrelationId());
        var buyerId = user.Identity!.Name!;

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest("A phone number is required.");
        }

        // Reject unusable destinations now rather than when a message fails to go out.
        var validation = await _smsProviderClient.ValidatePhoneNumberAsync(request.PhoneNumber);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            return Results.BadRequest($"The phone number is not a usable destination. {validation.ValidationError}".Trim());
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        if (existing.Any(c => c.PhoneNumber == validation.CanonicalNumber))
        {
            throw new DuplicateException("This phone number is already registered.");
        }

        var contactNumber = await _contactNumberRepository.AddAsync(new ContactNumber(buyerId, validation.CanonicalNumber));

        response.ContactNumberId = contactNumber.Id;
        response.PhoneNumber = contactNumber.PhoneNumber;
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
