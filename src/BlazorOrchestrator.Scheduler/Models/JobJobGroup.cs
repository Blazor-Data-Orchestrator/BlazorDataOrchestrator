using System;

namespace BlazorOrchestrator.Scheduler.Models
{
    public partial class JobJobGroup
    {
        public int Id { get; set; }
        public int JobId { get; set; }
        public int JobGroupId { get; set; }

        public virtual Jobs? Job { get; set; }
        public virtual JobGroups? JobGroup { get; set; }
    }
}
