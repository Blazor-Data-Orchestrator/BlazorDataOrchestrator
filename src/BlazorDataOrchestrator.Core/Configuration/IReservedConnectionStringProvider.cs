namespace BlazorDataOrchestrator.Core.Configuration;

/// <summary>
/// Supplies the host-owned reserved connection strings.
/// </summary>
public interface IReservedConnectionStringProvider
{
    ReservedConnectionStrings Get();
}
