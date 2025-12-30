import os
import json
import sys
import urllib.request
import urllib.error
from datetime import datetime
from typing import Optional

# Database connection (requires pyodbc for SQL Server)
try:
    import pyodbc
    HAS_PYODBC = True
except ImportError:
    HAS_PYODBC = False


class JobLogger:
    """Handles logging progress and errors to the database."""
    
    def __init__(self, connection_string: str, job_instance_id: int):
        self.connection_string = connection_string
        self.job_instance_id = job_instance_id
        self.connection = None
        
        if HAS_PYODBC and connection_string:
            try:
                self.connection = pyodbc.connect(connection_string)
            except Exception as e:
                print(f"Warning: Could not connect to database: {e}")
    
    def log_progress(self, message: str, level: str = "Info"):
        """Log progress message to console and database."""
        timestamp = datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S")
        print(f"[{timestamp}] [{level}] {message}")
        
        if self.connection and self.job_instance_id > 0:
            try:
                cursor = self.connection.cursor()
                
                # Get job_id from job_instance
                cursor.execute("""
                    SELECT js.JobId 
                    FROM JobInstances ji 
                    JOIN JobSchedules js ON ji.JobScheduleId = js.Id 
                    WHERE ji.Id = ?
                """, self.job_instance_id)
                row = cursor.fetchone()
                job_id = row[0] if row else 0
                
                if job_id > 0:
                    field_desc = f"Log_{level}_{datetime.utcnow().strftime('%Y%m%d%H%M%S')}"
                    cursor.execute("""
                        INSERT INTO JobData (JobId, JobFieldDescription, JobStringValue, CreatedDate, CreatedBy)
                        VALUES (?, ?, ?, GETUTCDATE(), 'JobExecutor')
                    """, job_id, field_desc, message)
                    self.connection.commit()
            except Exception as e:
                print(f"Warning: Failed to log to database: {e}")
    
    def log_error(self, message: str, stack_trace: str = ""):
        """Log error to console and database, update job instance status."""
        timestamp = datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S")
        print(f"[{timestamp}] [ERROR] {message}")
        
        if self.connection and self.job_instance_id > 0:
            try:
                cursor = self.connection.cursor()
                
                # Update job instance to mark as error
                cursor.execute("""
                    UPDATE JobInstances 
                    SET HasError = 1, UpdatedDate = GETUTCDATE(), UpdatedBy = 'JobExecutor'
                    WHERE Id = ?
                """, self.job_instance_id)
                
                # Get job_id and log error
                cursor.execute("""
                    SELECT js.JobId 
                    FROM JobInstances ji 
                    JOIN JobSchedules js ON ji.JobScheduleId = js.Id 
                    WHERE ji.Id = ?
                """, self.job_instance_id)
                row = cursor.fetchone()
                job_id = row[0] if row else 0
                
                if job_id > 0:
                    field_desc = f"Error_{datetime.utcnow().strftime('%Y%m%d%H%M%S')}"
                    error_message = f"{message}\n{stack_trace}" if stack_trace else message
                    cursor.execute("""
                        INSERT INTO JobData (JobId, JobFieldDescription, JobStringValue, CreatedDate, CreatedBy)
                        VALUES (?, ?, ?, GETUTCDATE(), 'JobExecutor')
                    """, job_id, field_desc, error_message)
                
                self.connection.commit()
            except Exception as e:
                print(f"Warning: Failed to log error to database: {e}")
    
    def close(self):
        """Close database connection."""
        if self.connection:
            self.connection.close()


def execute_job(app_settings: str, job_agent_id: int, job_id: int, job_instance_id: int, job_schedule_id: int):
    """
    Execute the job with the given parameters.
    
    Args:
        app_settings: JSON string containing application settings including connection strings
        job_agent_id: The ID of the job agent executing this job
        job_id: The ID of the job
        job_instance_id: The ID of this specific job instance
        job_schedule_id: The ID of the job schedule
    """
    # Parse connection string from app_settings
    connection_string = ""
    try:
        settings = json.loads(app_settings) if app_settings else {}
        connection_strings = settings.get("ConnectionStrings", {})
        connection_string = connection_strings.get("DefaultConnection", "")
    except json.JSONDecodeError:
        pass
    
    logger = JobLogger(connection_string, job_instance_id)
    
    try:
        logger.log_progress("Job started")
        print(f"Executing Job ID: {job_id}, Instance: {job_instance_id}, Schedule: {job_schedule_id}, Agent: {job_agent_id}")
        
        # Fetch weather data for Los Angeles, CA
        logger.log_progress("Fetching weather data for Los Angeles, CA")
        
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
                
        except urllib.error.URLError as e:
            error_msg = f"Failed to fetch weather data: {e.reason}"
            print(error_msg)
            logger.log_progress(error_msg, "Warning")
        except Exception as e:
            error_msg = f"Error processing weather data: {str(e)}"
            print(error_msg)
            logger.log_progress(error_msg, "Warning")
        
        logger.log_progress("Job completed successfully")
        
    except Exception as e:
        import traceback
        error_msg = f"Job execution error: {str(e)}"
        stack_trace = traceback.format_exc()
        logger.log_error(error_msg, stack_trace)
        raise
    finally:
        logger.close()
