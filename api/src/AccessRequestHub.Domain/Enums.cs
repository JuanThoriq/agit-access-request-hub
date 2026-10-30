namespace AccessRequestHub.Domain;

public enum EnvironmentType
{
    NonProduction,
    Production
}

public enum AccessLevel
{
    Read,
    Admin
}

/// <summary>
/// The only states a request can be in. Approved and Rejected are terminal.
/// </summary>
public enum RequestStatus
{
    PendingManagerApproval,
    PendingSystemOwnerApproval,
    Approved,
    Rejected
}
