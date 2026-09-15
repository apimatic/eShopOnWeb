using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in whatever form the caller typed it.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    public RegisterContactNumberResponse(int contactNumberId, string e164Number)
    {
        ContactNumberId = contactNumberId;
        E164Number = e164Number;
    }

    /// <summary>The identifier of the registered number (a top-level field so the flow can be driven end to end).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider-canonical E.164 form that was actually stored.</summary>
    public string E164Number { get; set; }
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a usable
/// destination is rejected here (not when a later message fails), and the provider's own canonical form is
/// what gets stored — not whatever the caller typed.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ISmsGateway, IRepository<ContactNumber>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RegisterContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ISmsGateway gateway, IRepository<ContactNumber> repository) =>
                await HandleAsync(request, gateway, repository))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ISmsGateway gateway, IRepository<ContactNumber> repository)
    {
        var owner = _httpContextAccessor.GetUserName();
        if (string.IsNullOrEmpty(owner))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        var ct = _httpContextAccessor.RequestAborted();

        // Reject an unusable destination now, and canonicalize to the provider's E.164 form.
        var validation = await gateway.ValidateNumberAsync(request.PhoneNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
        {
            return Results.BadRequest(new { message = "The number is not a usable destination.", reason = validation.Reason });
        }

        var canonical = validation.CanonicalE164!;

        // Registering the same number twice is idempotent — return the existing registration.
        var existing = await repository.FirstOrDefaultAsync(new ContactNumberByOwnerAndValueSpecification(owner, canonical), ct);
        if (existing is not null)
        {
            return Results.Ok(new RegisterContactNumberResponse(existing.Id, existing.E164Number));
        }

        var contactNumber = new ContactNumber(owner, canonical);
        contactNumber = await repository.AddAsync(contactNumber, ct);

        var response = new RegisterContactNumberResponse(contactNumber.Id, contactNumber.E164Number);
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
