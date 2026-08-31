using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// messaging provider first; what gets stored is the provider's canonical form of it.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ITextMessagingService _messagingService;

    public CreateContactNumberEndpoint(
        IRepository<ContactNumber> contactNumberRepository,
        ITextMessagingService messagingService)
    {
        _contactNumberRepository = contactNumberRepository;
        _messagingService = messagingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, System.Security.Claims.ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user.GetBuyerId());
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, string buyerId)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest("A phone number is required.");
        }

        ValidatedPhoneNumber validated;
        try
        {
            validated = await _messagingService.ValidatePhoneNumberAsync(request.PhoneNumber);
        }
        catch (MessagingProviderException ex) when (ex.StatusCode is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
        {
            return Results.BadRequest("The phone number could not be validated as a usable destination.");
        }

        if (!validated.IsValid || validated.CanonicalNumber is null)
        {
            return Results.BadRequest($"The phone number is not a usable destination: {string.Join(", ", validated.ValidationErrors)}");
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        if (existing.Any(c => c.PhoneNumber == validated.CanonicalNumber))
        {
            return Results.Conflict("This number is already registered.");
        }

        var contactNumber = new ContactNumber(buyerId, validated.CanonicalNumber);
        await _contactNumberRepository.AddAsync(contactNumber);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
