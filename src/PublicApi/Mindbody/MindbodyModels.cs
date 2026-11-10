using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

public class MbPagination
{
    public int RequestedLimit { get; set; }
    public int RequestedOffset { get; set; }
    public int PageSize { get; set; }
    public int TotalResults { get; set; }
}

public class MbPaged { public MbPagination? PaginationResponse { get; set; } }

public class MbSite
{
    public string? TimeZone { get; set; }
}
public class MbSitesResponse { public List<MbSite>? Sites { get; set; } }

public class MbLocation
{
    public int Id { get; set; }
    public string? Name { get; set; }
}
public class MbLocationsResponse : MbPaged { public List<MbLocation>? Locations { get; set; } }

public class MbSessionType
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public int? DefaultTimeLength { get; set; }
    public int? StaffTimeLength { get; set; }
}
public class MbSessionTypesResponse : MbPaged { public List<MbSessionType>? SessionTypes { get; set; } }

public class MbReferralTypesResponse { public List<string>? ReferralTypes { get; set; } }

public class MbProgram { public string? ScheduleType { get; set; } }

public class MbClassDescription
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public MbProgram? Program { get; set; }
}

public class MbStaff
{
    public long Id { get; set; }
    public bool Active { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? MobilePhone { get; set; }
    public string? HomePhone { get; set; }
    public string? WorkPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Bio { get; set; }
    public string? ImageUrl { get; set; }
    public bool ClassTeacher { get; set; }
    public bool AppointmentInstructor { get; set; }
}
public class MbStaffResponse : MbPaged { public List<MbStaff>? StaffMembers { get; set; } }

public class MbClass
{
    public int Id { get; set; }
    public bool IsCanceled { get; set; }
    public bool Active { get; set; }
    public bool IsAvailable { get; set; }
    public int? MaxCapacity { get; set; }
    public int? TotalBooked { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public MbLocation? Location { get; set; }
    public MbClassDescription? ClassDescription { get; set; }
    public MbStaff? Staff { get; set; }
}
public class MbClassesResponse : MbPaged { public List<MbClass>? Classes { get; set; } }

public class MbAvailability
{
    public MbStaff? Staff { get; set; }
    public MbSessionType? SessionType { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public DateTime? BookableEndDateTime { get; set; }
    public MbLocation? Location { get; set; }
}
public class MbAvailabilitiesResponse : MbPaged { public List<MbAvailability>? Availabilities { get; set; } }

public class MbClient
{
    public string? Id { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool Active { get; set; }
    public MbLocation? HomeLocation { get; set; }
}
public class MbClientsResponse : MbPaged { public List<MbClient>? Clients { get; set; } }
public class MbAddClientResponse { public MbClient? Client { get; set; } }

public class MbVisit
{
    public long Id { get; set; }
    public long? ClassId { get; set; }
    public long? AppointmentId { get; set; }
    public string? ClientId { get; set; }
    public string? AppointmentStatus { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
}
public class MbVisitsResponse : MbPaged { public List<MbVisit>? Visits { get; set; } }
public class MbAddToClassResponse { public MbVisit? Visit { get; set; } }

public class MbAppointment
{
    public long Id { get; set; }
    public string? Status { get; set; }
    public string? ClientId { get; set; }
    public int SessionTypeId { get; set; }
    public int LocationId { get; set; }
    public long StaffId { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
}
public class MbAppointmentsResponse : MbPaged { public List<MbAppointment>? Appointments { get; set; } }
public class MbAddAppointmentResponse { public MbAppointment? Appointment { get; set; } }

public class MbErrorResponse { public MbError? Error { get; set; } }
public class MbError
{
    public string? Message { get; set; }
    public string? Code { get; set; }
}
