using System.Linq;
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
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// messaging provider up front; the provider's canonical form is what gets stored.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal>
{
    private readonly IMessageProvider _messageProvider;
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public CreateContactNumberEndpoint(IMessageProvider messageProvider, IRepository<ContactNumber> contactNumberRepository)
    {
        _messageProvider = messageProvider;
        _contactNumberRepository = contactNumberRepository;
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
        var ownerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var validated = await _messageProvider.ValidateNumberAsync(request.PhoneNumber);
        if (!validated.IsValid || validated.CanonicalNumber is null)
        {
            var reasons = validated.ValidationErrors.Count > 0
                ? string.Join(", ", validated.ValidationErrors)
                : "the provider does not consider it a usable destination";
            throw new InvalidContactNumberException($"The phone number cannot receive messages: {reasons}.");
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        if (existing.Any(c => c.PhoneNumber == validated.CanonicalNumber))
        {
            throw new DuplicateException("This phone number is already registered.");
        }

        var contactNumber = new ContactNumber(ownerId, validated.CanonicalNumber);
        contactNumber = await _contactNumberRepository.AddAsync(contactNumber);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
