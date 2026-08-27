using System.Linq;
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
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// provider first; the provider's canonical form is what gets stored.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, HttpContext, IRepository<ContactNumber>>
{
    private readonly IMessagingProvider _messagingProvider;

    public RegisterContactNumberEndpoint(IMessagingProvider messagingProvider)
    {
        _messagingProvider = messagingProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext httpContext, IRepository<ContactNumber> contactNumberRepository) =>
            {
                return await HandleAsync(request, httpContext, contactNumberRepository);
            })
            .Produces<ContactNumberDto>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, HttpContext httpContext, IRepository<ContactNumber> contactNumberRepository)
    {
        var buyerId = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { error = "A phone number is required." });
        }

        var validation = await _messagingProvider.ValidatePhoneNumberAsync(request.PhoneNumber, httpContext.RequestAborted);
        if (!validation.IsValid || validation.CanonicalNumber == null)
        {
            return Results.BadRequest(new { error = $"The number was rejected by the messaging provider: {validation.Error}" });
        }

        var existingSpec = new ContactNumbersByBuyerSpecification(buyerId);
        var existing = await contactNumberRepository.ListAsync(existingSpec, httpContext.RequestAborted);
        if (existing.Any(n => n.PhoneNumber == validation.CanonicalNumber))
        {
            return Results.Conflict(new { error = "This number is already registered." });
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        contactNumber = await contactNumberRepository.AddAsync(contactNumber, httpContext.RequestAborted);

        var dto = new ContactNumberDto
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedAt = contactNumber.CreatedAt
        };
        return Results.Created($"api/contact-numbers/{dto.ContactNumberId}", dto);
    }
}
