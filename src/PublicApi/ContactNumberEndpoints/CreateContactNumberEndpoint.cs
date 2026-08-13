using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    /// <summary>Top-level identifier of the created contact number.</summary>
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The provider validates it and its
/// canonical E.164 form is stored; an unusable number is rejected here (HTTP 400).
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
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
            (CreateContactNumberRequest request, IContactNumberService service) =>
                await HandleAsync(request, service))
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        var contactNumber = await service.RegisterAsync(buyerId, request.PhoneNumber);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
