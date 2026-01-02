import os
import json
import sys
import urllib.request
import urllib.error
from datetime import datetime
from typing import Optional
import uuid

# Database connection (requires pyodbc for SQL Server)
try:
    import pyodbc
    HAS_PYODBC = True
except ImportError:
    HAS_PYODBC = False

# Azure Table Storage (requires azure-data-tables)
# Ensure you have installed the dependencies listed in requirements.txt
try:
    from azure.data.tables import TableServiceClient, TableEntity
    HAS_AZURE_TABLES = True
except ImportError:
    HAS_AZURE_TABLES = False


class JobLogger:
    """Handles logging progress and errors to the database and Azure Table Storage.
    Logs are partitioned by '{JobId}-{JobInstanceId}' for efficient querying."""
    
    def __init__(self, connection_string: str, job_instance_id: int, table_connection_string: str = None):
        self.connection_string = connection_string
        self.job_instance_id = job_instance_id
        self.table_connection_string = table_connection_string
        self.connection = None
        self.job_id = 0
        self.table_client = None
        
        if HAS_PYODBC and connection_string:
            try:
                # Fix for ODBC requiring 'yes'/'no' instead of 'true'/'false'
                connection_string = connection_string.replace("TrustServerCertificate=true", "TrustServerCertificate=yes")
                connection_string = connection_string.replace("TrustServerCertificate=True", "TrustServerCertificate=yes")
                connection_string = connection_string.replace("TrustServerCertificate=false", "TrustServerCertificate=no")
                connection_string = connection_string.replace("TrustServerCertificate=False", "TrustServerCertificate=no")
                
                connection_string = connection_string.replace("Encrypt=true", "Encrypt=yes")
                connection_string = connection_string.replace("Encrypt=True", "Encrypt=yes")
                connection_string = connection_string.replace("Encrypt=false", "Encrypt=no")
                connection_string = connection_string.replace("Encrypt=False", "Encrypt=no")

                # Fix for ODBC using UID/PWD instead of User ID/Password
                connection_string = connection_string.replace("User ID=", "UID=")
                connection_string = connection_string.replace("User Id=", "UID=")
                connection_string = connection_string.replace("Password=", "PWD=")

                # Ensure driver is specified in connection string
                if "Driver={" not in connection_string:
                    drivers = pyodbc.drivers()
                    if "ODBC Driver 17 for SQL Server" in drivers:
                        connection_string += ";Driver={ODBC Driver 17 for SQL Server};"
                    elif "ODBC Driver 18 for SQL Server" in drivers:
                        connection_string += ";Driver={ODBC Driver 18 for SQL Server};TrustServerCertificate=yes;"
                    elif "SQL Server" in drivers:
                        connection_string += ";Driver={SQL Server};"

                self.connection = pyodbc.connect(connection_string)
                # Get job_id from job_instance_id
                self.job_id = self._get_job_id_from_instance()
            except Exception as e:
                print(f"Warning: Could not connect to database: {e}")
        
        # Initialize Azure Table Storage client
        if not HAS_AZURE_TABLES:
             print("[ERROR!] azure-data-tables package is not installed. Cannot log to Azure Table Storage.")
        elif not table_connection_string:
             print("[ERROR!] Table connection string is missing. Cannot log to Azure Table Storage.")
        else:
            try:
                table_service = TableServiceClient.from_connection_string(table_connection_string)
                self.table_client = table_service.create_table_if_not_exists("JobLogs")
            except Exception as e:
                print(f"[ERROR!] Could not connect to Azure Table Storage: {e}")
    
    def _get_job_id_from_instance(self) -> int:
        """Get the job_id from the job_instance_id."""
        if not self.connection or self.job_instance_id <= 0:
            return 0
        try:
            cursor = self.connection.cursor()
            cursor.execute("""
                SELECT js.JobId 
                FROM JobInstance ji 
                JOIN JobSchedule js ON ji.JobScheduleId = js.Id 
                WHERE ji.Id = ?
            """, self.job_instance_id)
            row = cursor.fetchone()
            return row[0] if row else 0
        except Exception as e:
            print(f"Warning: Could not get job_id from instance: {e}")
            return 0
    
    def _get_partition_key(self) -> str:
        """Get the partition key in format '{JobId}-{JobInstanceId}'."""
        return f"{self.job_id}-{self.job_instance_id}"
    
    def log_progress(self, message: str, level: str = "Info"):
        """Log progress message to console, database, and Azure Table Storage."""
        timestamp = datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S")
        print(f"[{timestamp}] [{level}] {message}")
               
        # Log to Azure Table Storage with partition key "{JobId}-{JobInstanceId}"
        if self.table_client and self.job_id > 0:
            try:
                entity = {
                    "PartitionKey": self._get_partition_key(),
                    "RowKey": str(uuid.uuid4()),
                    "Action": "JobProgress",
                    "Details": message,
                    "Level": level,
                    "Timestamp": datetime.utcnow(),
                    "JobId": self.job_id,
                    "JobInstanceId": self.job_instance_id
                }
                self.table_client.create_entity(entity)
            except Exception as e:
                print(f"[ERROR!] Failed to log to Azure Table Storage: {e}")
    
    def log_error(self, message: str, stack_trace: str = ""):
        """Log error to console, database, and Azure Table Storage, update job instance status."""
        timestamp = datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S")
        print(f"[{timestamp}] [ERROR] {message}")
        
        # Log to SQL Server database
        if self.connection and self.job_instance_id > 0:
            try:
                cursor = self.connection.cursor()
                
                # Update job instance to mark as error
                cursor.execute("""
                    UPDATE JobInstances 
                    SET HasError = 1, UpdatedDate = GETUTCDATE(), UpdatedBy = 'JobExecutor'
                    WHERE Id = ?
                """, self.job_instance_id)
                
                if self.job_id > 0:
                    field_desc = f"Error_{datetime.utcnow().strftime('%Y%m%d%H%M%S')}_{uuid.uuid4().hex[:8]}"
                    error_message = f"{message}\n{stack_trace}" if stack_trace else message
                    cursor.execute("""
                        INSERT INTO JobData (JobId, JobFieldDescription, JobStringValue, CreatedDate, CreatedBy)
                        VALUES (?, ?, ?, GETUTCDATE(), 'JobExecutor')
                    """, self.job_id, field_desc, error_message)
                
                self.connection.commit()
            except Exception as e:
                print(f"Warning: Failed to log error to database: {e}")
        
        # Log to Azure Table Storage with partition key "{JobId}-{JobInstanceId}"
        if self.table_client and self.job_id > 0:
            try:
                error_message = f"{message}\n{stack_trace}" if stack_trace else message
                entity = {
                    "PartitionKey": self._get_partition_key(),
                    "RowKey": str(uuid.uuid4()),
                    "Action": "JobError",
                    "Details": error_message,
                    "Level": "Error",
                    "Timestamp": datetime.utcnow(),
                    "JobId": self.job_id,
                    "JobInstanceId": self.job_instance_id
                }
                self.table_client.create_entity(entity)
            except Exception as e:
                print(f"[ERROR!] Failed to log error to Azure Table Storage: {e}")
    
    def close(self):
        """Close database connection."""
        if self.connection:
            self.connection.close()


def execute_job(app_settings: str, job_agent_id: int, job_id: int, job_instance_id: int, job_schedule_id: int) -> list[str]:
    """
    Execute the job with the given parameters.
    Logs are partitioned by '{JobId}-{JobInstanceId}' for efficient querying.
    
    Args:
        app_settings: JSON string containing application settings including connection strings
        job_agent_id: The ID of the job agent executing this job
        job_id: The ID of the job
        job_instance_id: The ID of this specific job instance
        job_schedule_id: The ID of the job schedule
    """
    logs = []
    # Parse connection strings from app_settings
    connection_string = ""
    table_connection_string = ""
    try:
        settings = json.loads(app_settings) if app_settings else {}
        connection_strings = settings.get("ConnectionStrings", {})
        connection_string = connection_strings.get("blazororchestratordb", "")
        table_connection_string = connection_strings.get("tables", "")
    except json.JSONDecodeError:
        pass
    
    logger = JobLogger(connection_string, job_instance_id, table_connection_string)
    
    # Get job_id from job_instance_id if not provided
    if job_id <= 0 and logger.job_id > 0:
        job_id = logger.job_id
    
    try:
        logger.log_progress("Job started")
        logs.append("Job started")
        
        exec_info = f"Executing Job ID: {job_id}, Instance: {job_instance_id}, Schedule: {job_schedule_id}, Agent: {job_agent_id}"
        print(exec_info)
        logger.log_progress(exec_info)
        logs.append(exec_info)
        
        partition_key_info = f"Log partition key: {job_id}-{job_instance_id}"
        print(partition_key_info)
        logger.log_progress(partition_key_info)
        logs.append(partition_key_info)
        
        # Check for previous run time
        if logger.connection and job_id > 0:
            try:
                cursor = logger.connection.cursor()
                cursor.execute("""
                    SELECT JobDateValue 
                    FROM JobData 
                    WHERE JobId = ? AND JobFieldDescription = 'Last Job Run Time'
                """, job_id)
                row = cursor.fetchone()
                if row and row[0]:
                    prev_run_time = row[0]
                    # Convert to local time (assuming prev_run_time is UTC)
                    local_time = prev_run_time + (datetime.now() - datetime.utcnow())
                    # Format: 00/00/0000 00:00(am/pm) -> %m/%d/%Y %I:%M%p
                    formatted_time = local_time.strftime("%m/%d/%Y %I:%M") + local_time.strftime("%p").lower()
                    prev_run_msg = f"Previous time the job was run: {formatted_time}"
                    print(prev_run_msg)
                    logger.log_progress(prev_run_msg)
                    logs.append(prev_run_msg)
            except Exception as e:
                print(f"Warning: Failed to read Last Job Run Time: {e}")
        
        # Fetch weather data for Los Angeles, CA
        logger.log_progress("Fetching weather data for Los Angeles, CA")
        logs.append("Fetching weather data for Los Angeles, CA")
        
        # Using wttr.in as a free weather API (weather.com requires API key)
        weather_url = "https://wttr.in/Los+Angeles,CA?format=j1"
        
        try:
            req = urllib.request.Request(
                weather_url,
                headers={"User-Agent": "BlazorDataOrchestrator/1.0"}
            )
            with urllib.request.urlopen(req, timeout=30) as response:
                weather_data = json.loads(response.read().decode("utf-8"))
                
                # Extract current weather information
                current_condition = weather_data["current_condition"][0]
                temp_c = current_condition["temp_C"]
                temp_f = current_condition["temp_F"]
                humidity = current_condition["humidity"]
                weather_desc = current_condition["weatherDesc"][0]["value"]
                
                weather_info = f"Los Angeles, CA - Temperature: {temp_f}°F ({temp_c}°C), Humidity: {humidity}%, Conditions: {weather_desc}"
                print(weather_info)
                logger.log_progress(weather_info)
                logs.append(weather_info)
                
        except urllib.error.URLError as e:
            error_msg = f"Failed to fetch weather data: {e.reason}"
            print(error_msg)
            logger.log_progress(error_msg, "Warning")
            logs.append(error_msg)
        except Exception as e:
            error_msg = f"Error processing weather data: {str(e)}"
            print(error_msg)
            logger.log_progress(error_msg, "Warning")
            logs.append(error_msg)
        
        logger.log_progress("Job completed successfully!")
        logs.append("Job completed successfully!")
        
    except Exception as e:
        import traceback
        error_msg = f"Job execution error: {str(e)}"
        stack_trace = traceback.format_exc()
        logger.log_error(error_msg, stack_trace)
        logs.append(error_msg)
        raise
    finally:
        # Update Last Job Run Time
        if logger.connection and job_id > 0:
            try:
                cursor = logger.connection.cursor()
                cursor.execute("""
                    SELECT Id FROM JobData 
                    WHERE JobId = ? AND JobFieldDescription = 'Last Job Run Time'
                """, job_id)
                row = cursor.fetchone()
                
                current_time = datetime.utcnow()
                
                if row:
                    cursor.execute("""
                        UPDATE JobData 
                        SET JobDateValue = ?, UpdatedDate = GETUTCDATE(), UpdatedBy = 'JobExecutor'
                        WHERE Id = ?
                    """, current_time, row[0])
                else:
                    cursor.execute("""
                        INSERT INTO JobData (JobId, JobFieldDescription, JobDateValue, CreatedDate, CreatedBy)
                        VALUES (?, 'Last Job Run Time', ?, GETUTCDATE(), 'JobExecutor')
                    """, job_id, current_time)
                logger.connection.commit()
            except Exception as e:
                print(f"Warning: Failed to update Last Job Run Time: {e}")

        logger.close()
    
    return logs
