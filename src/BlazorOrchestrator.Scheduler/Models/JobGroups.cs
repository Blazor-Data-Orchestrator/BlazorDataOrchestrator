using System;
using System.Collections.Generic;

namespace BlazorOrchestrator.Scheduler.Models
{
    public partial class JobGroups
    {
        public JobGroups()
        {
            JobJobGroups = new HashSet<JobJobGroup>();
        }

        public int Id { get; set; }
        public string JobGroupName { get; set; } = null!;
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = null!;
        public DateTime? UpdatedDate { get; set; }
        public string? UpdatedBy { get; set; }

        public virtual ICollection<JobJobGroup> JobJobGroups { get; set; }
    }
}
