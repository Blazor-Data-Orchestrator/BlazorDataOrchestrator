using System;
using System.Collections.Generic;

namespace BlazorOrchestrator.Scheduler.Models
{
    public partial class JobSchedule
    {
        public JobSchedule()
        {
            JobInstances = new HashSet<JobInstance>();
        }

        public int Id { get; set; }
        public int JobId { get; set; }
        public string ScheduleName { get; set; } = null!;
        public bool Enabled { get; set; }
        public bool InProcess { get; set; }
        public bool HadError { get; set; }
        public DateTime? LastRun { get; set; }
        public int? RunEveryHour { get; set; }
        public int? StartTime { get; set; }
        public int? StopTime { get; set; }
        public bool Monday { get; set; }
        public bool Tuesday { get; set; }
        public bool Wednesday { get; set; }
        public bool Thursday { get; set; }
        public bool Friday { get; set; }
        public bool Saturday { get; set; }
        public bool Sunday { get; set; }
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = null!;
        public DateTime? UpdatedDate { get; set; }
        public string? UpdatedBy { get; set; }

        public virtual Jobs? Job { get; set; }
        public virtual ICollection<JobInstance> JobInstances { get; set; }
    }
}
