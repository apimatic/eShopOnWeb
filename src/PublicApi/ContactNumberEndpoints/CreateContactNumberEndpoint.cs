using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
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
/// messaging provider first; what gets stored is the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal>
{
    private static readonly Regex PhoneNumberShape = new(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled);

    private readonly INotificationGateway _gateway;
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public CreateContactNumberEndpoint(INotificationGateway gateway,
        IRepository<ContactNumber> contactNumberRepository)
    {
        _gateway = gateway;
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
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, ClaimsPrincipal user)
    {
        var ownerId = user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var typed = request.PhoneNumber?.Trim() ?? string.Empty;
        if (!PhoneNumberShape.IsMatch(typed))
        {
            return Results.BadRequest(new { message = "Phone number must be in international format (e.g. +14155552671)." });
        }

        var validation = await _gateway.ValidatePhoneNumberAsync(typed);
        if (validation.Validity == PhoneNumberValidity.Invalid)
        {
            return Results.UnprocessableEntity(new
            {
                message = "The messaging provider does not consider this a usable destination.",
                errors = validation.ValidationErrors
            });
        }

        var canonical = validation.Validity == PhoneNumberValidity.Valid
            ? validation.CanonicalNumber!
            : typed;

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
        if (existing.Any(c => c.PhoneNumber == canonical))
        {
            throw new DuplicateException("This phone number is already registered.");
        }

        var contactNumber = new ContactNumber(ownerId, canonical, validation.Validity == PhoneNumberValidity.Valid);
        contactNumber = await _contactNumberRepository.AddAsync(contactNumber);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            IsVerified = contactNumber.IsVerified
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
