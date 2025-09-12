# Database Health Check Fix

## Problem
The SQL Server database resource was running but showing as unhealthy, causing all dependent services to wait indefinitely. This was due to a complex initialization script that included stored procedures, indexes, and other advanced features that took too long to execute during the database health check phase.

## Solution

### 1. Simplified Initialization Script
The original `01.00.00.sql` script has been split into two parts:

- **`01.00.00-simple.sql`**: Contains basic table creation and foreign key constraints
- **`02.00.00-advanced.sql`**: Contains complex operations like stored procedures and columnstore indexes

### 2. Updated Configuration
- Added `MSSQL_PID=Developer` environment variable for better SQL Server compatibility
- Using the simplified script for initial database creation to pass health checks faster

### 3. Running Advanced Features
To apply the advanced database features after the application is running:

#### Option 1: Manual Execution
1. Connect to the SQL Server container using SQL Server Management Studio or Azure Data Studio
2. Execute the `02.00.00-advanced.sql` script manually

#### Option 2: Migration Service (Future Enhancement)
The `DatabaseMigrationService.cs` has been created as a foundation for automatic advanced migrations. This can be enhanced to run after the database is healthy.

## Files Modified
- `Program.cs`: Updated to use simplified SQL script
- `BlazorOrchistrator.AppHost.csproj`: Added new SQL files to project
- `01.00.00-simple.sql`: New simplified initialization script
- `02.00.00-advanced.sql`: Advanced features script

## Connection String
The database will be available with the connection string referenced as "database" in your application projects.

## Testing
1. Start the application with `dotnet run`
2. Verify that the database resource shows as healthy in the Aspire dashboard
3. Verify that dependent services (webapp, scheduler, agent) start successfully
4. Optionally run the advanced features script for full functionality