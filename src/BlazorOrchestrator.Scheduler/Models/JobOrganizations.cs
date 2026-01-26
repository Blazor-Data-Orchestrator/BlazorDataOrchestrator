using System;
using System.Collections.Generic;

namespace BlazorOrchestrator.Scheduler.Models
{
    public partial class JobOrganizations
    {
        public JobOrganizations()
        {
            Jobs = new HashSet<Jobs>();
        }

        public int Id { get; set; }
        public string OrganizationName { get; set; } = null!;
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = null!;
        public DateTime? UpdatedDate { get; set; }
        public string? UpdatedBy { get; set; }

        public virtual ICollection<Jobs> Jobs { get; set; }
    }
}
