using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated
/// by the messaging provider up front and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly ISmsProvider _smsProvider;

    public CreateContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository, ISmsProvider smsProvider)
    {
        _contactNumberRepository = contactNumberRepository;
        _smsProvider = smsProvider;
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
            return Results.BadRequest(new { error = "phoneNumber is required." });
        }

        // Reject unusable destinations now, not when a message later fails to go out.
        var validation = await _smsProvider.ValidatePhoneNumberAsync(request.PhoneNumber);
        if (!validation.IsValid || validation.CanonicalNumber is null)
        {
            return Results.BadRequest(new { error = "The phone number is not a usable destination.", validationErrors = validation.ValidationErrors });
        }

        // Store the provider's canonical form, not what the caller typed.
        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        await _contactNumberRepository.AddAsync(contactNumber);

        return Results.Created($"api/contact-numbers/{contactNumber.Id}", new CreateContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        });
    }
}
