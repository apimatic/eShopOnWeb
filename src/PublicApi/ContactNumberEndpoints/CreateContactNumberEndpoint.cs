using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// with the provider up front and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest>
{
    private readonly IPhoneNumberValidator _phoneNumberValidator;
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public CreateContactNumberEndpoint(IPhoneNumberValidator phoneNumberValidator,
        IRepository<ContactNumber> contactNumberRepository)
    {
        _phoneNumberValidator = phoneNumberValidator;
        _contactNumberRepository = contactNumberRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, claimsPrincipal, cancellationToken);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request)
        => HandleAsync(request, null, default);

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, ClaimsPrincipal? claimsPrincipal, CancellationToken cancellationToken)
    {
        var ownerId = claimsPrincipal?.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { error = "A phone number is required." });
        }

        var validation = await _phoneNumberValidator.ValidateAsync(request.PhoneNumber, cancellationToken);
        if (!validation.IsValid || validation.CanonicalNumber is null)
        {
            return Results.BadRequest(new
            {
                error = "The phone number is not a usable destination.",
                validationErrors = validation.ValidationErrors
            });
        }

        var existingSpec = new ContactNumbersByOwnerSpecification(ownerId);
        var existing = (await _contactNumberRepository.ListAsync(existingSpec, cancellationToken))
            .FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (existing is not null)
        {
            return Results.Ok(new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = existing.Id,
                PhoneNumber = existing.PhoneNumber
            });
        }

        var contactNumber = new ContactNumber(ownerId, validation.CanonicalNumber);
        contactNumber = await _contactNumberRepository.AddAsync(contactNumber, cancellationToken);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
