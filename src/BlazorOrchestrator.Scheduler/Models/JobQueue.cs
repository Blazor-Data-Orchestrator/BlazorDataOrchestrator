using System;
using System.Collections.Generic;

namespace BlazorOrchestrator.Scheduler.Models
{
    public partial class JobQueue
    {
        public JobQueue()
        {
            Jobs = new HashSet<Jobs>();
        }

        public int Id { get; set; }
        public string QueueName { get; set; } = null!;
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = null!;

        public virtual ICollection<Jobs> Jobs { get; set; }
    }
}
