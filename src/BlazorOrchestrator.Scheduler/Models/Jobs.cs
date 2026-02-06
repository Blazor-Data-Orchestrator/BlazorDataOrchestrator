using System;
using System.Collections.Generic;

namespace BlazorOrchestrator.Scheduler.Models
{
    public partial class Jobs
    {
        public Jobs()
        {
            JobDataItems = new HashSet<JobData>();
            JobJobGroups = new HashSet<JobJobGroup>();
            JobSchedules = new HashSet<JobSchedule>();
        }

        public int Id { get; set; }
        public int JobOrganizationId { get; set; }
        public string JobName { get; set; } = null!;
        public string JobEnvironment { get; set; } = null!;
        public bool JobEnabled { get; set; }
        public bool JobQueued { get; set; }
        public bool JobInProcess { get; set; }
        public bool JobInError { get; set; }
        public string JobCodeFile { get; set; } = null!;
        public int? JobQueue { get; set; }
        public string? WebhookGUID { get; set; }
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = null!;
        public DateTime? UpdatedDate { get; set; }
        public string? UpdatedBy { get; set; }

        public virtual JobOrganizations? JobOrganization { get; set; }
        public virtual JobQueue? JobQueueNavigation { get; set; }
        public virtual ICollection<JobData> JobDataItems { get; set; }
        public virtual ICollection<JobJobGroup> JobJobGroups { get; set; }
        public virtual ICollection<JobSchedule> JobSchedules { get; set; }
    }
}
