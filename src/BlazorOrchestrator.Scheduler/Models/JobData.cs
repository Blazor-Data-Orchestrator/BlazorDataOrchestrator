using System;

namespace BlazorOrchestrator.Scheduler.Models
{
    public partial class JobData
    {
        public int Id { get; set; }
        public int JobId { get; set; }
        public string JobFieldDescription { get; set; } = null!;
        public int? JobIntValue { get; set; }
        public string? JobStringValue { get; set; }
        public DateTime? JobDateValue { get; set; }
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = null!;
        public DateTime? UpdatedDate { get; set; }
        public string? UpdatedBy { get; set; }

        public virtual Jobs? Job { get; set; }
    }
}
