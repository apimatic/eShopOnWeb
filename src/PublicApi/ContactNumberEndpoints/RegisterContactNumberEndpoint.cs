using System.Security.Claims;
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
/// Registers a mobile contact number for the signed-in shopper. The number is validated
/// with the messaging provider and stored in the provider's canonical form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ClaimsPrincipal>
{
    private readonly IPhoneNumberValidator _phoneNumberValidator;
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public RegisterContactNumberEndpoint(IPhoneNumberValidator phoneNumberValidator,
        IRepository<ContactNumber> contactNumberRepository)
    {
        _phoneNumberValidator = phoneNumberValidator;
        _contactNumberRepository = contactNumberRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal claimsPrincipal) =>
            {
                return await HandleAsync(request, claimsPrincipal);
            })
            .Produces<RegisterContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal claimsPrincipal)
    {
        var buyerId = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var validation = await _phoneNumberValidator.ValidateAsync(request.PhoneNumber);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new RegisterContactNumberResponse(request.CorrelationId())
            {
                Error = validation.Error ?? "The phone number is not a usable destination."
            });
        }

        var canonicalNumber = validation.CanonicalNumber!;

        var existingSpec = new ContactNumberByBuyerAndNumberSpecification(buyerId, canonicalNumber);
        var existing = await _contactNumberRepository.FirstOrDefaultAsync(existingSpec);
        if (existing != null)
        {
            return Results.Ok(new RegisterContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = existing.Id,
                PhoneNumber = existing.PhoneNumber,
                CreatedAt = existing.CreatedAt
            });
        }

        var contactNumber = new ContactNumber(buyerId, canonicalNumber);
        await _contactNumberRepository.AddAsync(contactNumber);

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedAt = contactNumber.CreatedAt
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(System.Guid correlationId) : base(correlationId) {}
    public RegisterContactNumberResponse() {}

    public int ContactNumberId { get; set; }
    public string? PhoneNumber { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }
    public string? Error { get; set; }
}
