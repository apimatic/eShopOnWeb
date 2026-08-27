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
/// Registers a mobile contact number for the signed-in shopper. The number is
/// validated with the messaging provider and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IMessageProvider _messageProvider;

    public CreateContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository, IMessageProvider messageProvider)
    {
        _contactNumberRepository = contactNumberRepository;
        _messageProvider = messageProvider;
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
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        var validation = await _messageProvider.ValidatePhoneNumberAsync(request.PhoneNumber);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            return Results.BadRequest(new
            {
                message = "The phone number is not a usable destination.",
                validationErrors = validation.ValidationErrors
            });
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (duplicate != null)
        {
            var duplicateResponse = new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = duplicate.Id,
                PhoneNumber = duplicate.PhoneNumber
            };
            return Results.Ok(duplicateResponse);
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        await _contactNumberRepository.AddAsync(contactNumber);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
