using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile contact number for the signed-in shopper. The number is validated
/// by the messaging provider up front and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IMessagingService, IRepository<ContactNumber>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, IMessagingService messagingService, IRepository<ContactNumber> contactNumberRepository) =>
            {
                return await HandleAsync(request, messagingService, contactNumberRepository);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IMessagingService messagingService,
        IRepository<ContactNumber> contactNumberRepository)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { error = "A phone number is required." });
        }

        var validation = await messagingService.ValidateNumberAsync(request.PhoneNumber);
        if (validation.FailureKind is MessagingFailureKind.Unreachable or MessagingFailureKind.UnprocessableResponse)
        {
            return Results.Problem("The messaging provider could not be reached to validate the number. Try again later.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            return Results.BadRequest(new
            {
                error = "The phone number is not a usable destination.",
                validationErrors = validation.ValidationErrors
            });
        }

        var existing = await contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        if (existing.Any(c => c.PhoneNumber == validation.CanonicalNumber))
        {
            return Results.Conflict(new { error = "This number is already registered." });
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        contactNumber = await contactNumberRepository.AddAsync(contactNumber);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedAt = contactNumber.CreatedAt
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
