using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(System.Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    /// <summary>The identifier of the registered number (top-level, so the flow can be driven end to end).</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/contact-numbers — registers a mobile number for the signed-in shopper. A number the
/// provider does not consider a usable destination is rejected here (400), and what gets stored is
/// the provider's own canonical form.
/// </summary>
public class RegisterContactNumberEndpoint : ApiEndpointBase,
    IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public RegisterContactNumberEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService service) =>
                await HandleAsync(request, service))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        var ownerId = CallerId;
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { message = "A phone number is required." });

        var result = await service.RegisterAsync(ownerId, request.PhoneNumber, Aborted);
        if (result.Rejected || result.ContactNumber is null)
            return Results.BadRequest(new { message = "The number is not a usable destination and was not registered." });

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = result.ContactNumber.Id,
            PhoneNumber = result.ContactNumber.CanonicalNumber
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}
