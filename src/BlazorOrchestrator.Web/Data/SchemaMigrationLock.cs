using System.Data;
using Microsoft.Data.SqlClient;

namespace BlazorOrchestrator.Web.Data;

/// <summary>
/// Database-wide lock around every schema change (new-database baseline and Upgrade Wizard
/// scripts). It is a SQL Server application lock (<c>sp_getapplock</c>) owned by the session of
/// the connection that runs the scripts, so it works across replicas and processes, not only
/// within this one. Callers read the schema version only after the lock is held.
/// </summary>
public sealed class SchemaMigrationLock : IAsyncDisposable
{
    public const string ResourceName = "BlazorOrchestrator.SchemaMigration";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly SqlConnection _connection;
    private bool _released;

    private SchemaMigrationLock(SqlConnection connection) => _connection = connection;

    /// <summary>
    /// Waits for the lock on <paramref name="connection"/> (which must stay open while the lock is
    /// held). Throws <see cref="SchemaMigrationLockTimeoutException"/> when another schema change
    /// keeps it longer than <paramref name="timeout"/>.
    /// </summary>
    public static async Task<SchemaMigrationLock> AcquireAsync(SqlConnection connection, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var wait = timeout ?? DefaultTimeout;

        await using var command = new SqlCommand("sp_getapplock", connection)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = (int)wait.TotalSeconds + 30
        };
        command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = ResourceName });
        command.Parameters.Add(new SqlParameter("@LockMode", SqlDbType.VarChar, 32) { Value = "Exclusive" });
        command.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.VarChar, 32) { Value = "Session" });
        command.Parameters.Add(new SqlParameter("@LockTimeout", SqlDbType.Int) { Value = (int)wait.TotalMilliseconds });
        command.Parameters.Add(new SqlParameter("@DbPrincipal", SqlDbType.NVarChar, 128) { Value = "public" });
        var result = command.Parameters.Add(new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue });

        await command.ExecuteNonQueryAsync(cancellationToken);

        var code = (int)result.Value;
        return code switch
        {
            >= 0 => new SchemaMigrationLock(connection),
            -1 => throw new SchemaMigrationLockTimeoutException(
                $"Another database upgrade is still running (waited {wait.TotalSeconds:0} seconds). Try again when it has finished."),
            _ => throw new InvalidOperationException($"Could not acquire the schema migration lock (sp_getapplock returned {code}).")
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_released) return;
        _released = true;

        try
        {
            if (_connection.State == ConnectionState.Open)
            {
                await using var command = new SqlCommand("sp_releaseapplock", _connection) { CommandType = CommandType.StoredProcedure };
                command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255) { Value = ResourceName });
                command.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.VarChar, 32) { Value = "Session" });
                command.Parameters.Add(new SqlParameter("@DbPrincipal", SqlDbType.NVarChar, 128) { Value = "public" });
                await command.ExecuteNonQueryAsync();
            }
        }
        catch (SqlException)
        {
            // A broken session releases its locks when SQL Server closes it.
        }
    }
}

public sealed class SchemaMigrationLockTimeoutException(string message) : Exception(message);
