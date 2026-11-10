using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Studio;

// Wire types for the parts of the Mindbody Public API v6 that the studio features use.

public class MbSitesPage { public List<MbSite>? Sites { get; set; } }
public class MbSite { public string? TimeZone { get; set; } }

public class MbLocationsPage : MindbodyApi.IPaged
{
    public MindbodyApi.PaginationInfo? PaginationResponse { get; set; }
    public List<MbLocation>? Locations { get; set; }
}
public class MbLocation { public int Id { get; set; } public string? Name { get; set; } }

public class MbSessionTypesPage : MindbodyApi.IPaged
{
    public MindbodyApi.PaginationInfo? PaginationResponse { get; set; }
    public List<MbSessionType>? SessionTypes { get; set; }
}
public class MbSessionType
{
    public int Id { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public int? DefaultTimeLength { get; set; }
    public int? StaffTimeLength { get; set; }
    public int ProgramId { get; set; }
}

public class MbClassesPage : MindbodyApi.IPaged
{
    public MindbodyApi.PaginationInfo? PaginationResponse { get; set; }
    public List<MbClass>? Classes { get; set; }
}
public class MbClass
{
    public long Id { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public bool IsCanceled { get; set; }
    public bool Active { get; set; }
    public bool IsAvailable { get; set; }
    public int? WebCapacity { get; set; }
    public int? WebBooked { get; set; }
    public MbLocation? Location { get; set; }
    public MbStaff? Staff { get; set; }
    public MbClassDescription? ClassDescription { get; set; }
}
public class MbClassDescription
{
    public string? Name { get; set; }
    public MbProgram? Program { get; set; }
}
public class MbProgram { public int Id { get; set; } public string? ScheduleType { get; set; } }

public class MbStaffPage : MindbodyApi.IPaged
{
    public MindbodyApi.PaginationInfo? PaginationResponse { get; set; }
    public List<MbStaff>? StaffMembers { get; set; }
}
public class MbStaff
{
    public long Id { get; set; }
    public bool Active { get; set; }
    public bool AppointmentInstructor { get; set; }
    public bool ClassTeacher { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? HomePhone { get; set; }
    public string? MobilePhone { get; set; }
    public string? WorkPhone { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Bio { get; set; }
    public string? ImageUrl { get; set; }
    public List<MbAvailability>? Availabilities { get; set; }
}
public class MbAvailability
{
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public List<MbProgram>? Programs { get; set; }
    public MbLocation? Location { get; set; }
}

public class MbBookablePage : MindbodyApi.IPaged
{
    public MindbodyApi.PaginationInfo? PaginationResponse { get; set; }
    public List<MbBookable>? Availabilities { get; set; }
}
public class MbBookable
{
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public DateTime? BookableEndDateTime { get; set; }
    public MbStaff? Staff { get; set; }
    public MbLocation? Location { get; set; }
}

public class MbClientsPage : MindbodyApi.IPaged
{
    public MindbodyApi.PaginationInfo? PaginationResponse { get; set; }
    public List<MbClient>? Clients { get; set; }
}
public class MbClientResponse { public MbClient? Client { get; set; } }
public class MbClient
{
    public string? Id { get; set; }
    public string? Email { get; set; }
    public bool? Active { get; set; }
}

public class MbReferralTypesPage { public List<string>? ReferralTypes { get; set; } }

public class MbVisitsPage : MindbodyApi.IPaged
{
    public MindbodyApi.PaginationInfo? PaginationResponse { get; set; }
    public List<MbVisit>? Visits { get; set; }
}
public class MbVisit
{
    public long Id { get; set; }
    public long ClassId { get; set; }
    public long AppointmentId { get; set; }
    public string? AppointmentStatus { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public string? Name { get; set; }
    public int? LocationId { get; set; }
}
public class MbAddToClassResponse { public MbVisit? Visit { get; set; } }

public class MbAppointmentsPage : MindbodyApi.IPaged
{
    public MindbodyApi.PaginationInfo? PaginationResponse { get; set; }
    public List<MbAppointment>? Appointments { get; set; }
}
public class MbAppointment
{
    public long Id { get; set; }
    public string? Status { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public int SessionTypeId { get; set; }
    public long StaffId { get; set; }
    public int LocationId { get; set; }
    public string? ClientId { get; set; }
}
public class MbAppointmentResponse { public MbAppointment? Appointment { get; set; } }
