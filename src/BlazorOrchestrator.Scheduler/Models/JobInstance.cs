using System;

namespace BlazorOrchestrator.Scheduler.Models
{
    public partial class JobInstance
    {
        public int Id { get; set; }
        public int JobScheduleId { get; set; }
        public bool InProcess { get; set; }
        public bool HasError { get; set; }
        public string? AgentId { get; set; }
        public DateTime CreatedDate { get; set; }
        public string CreatedBy { get; set; } = null!;
        public DateTime? UpdatedDate { get; set; }
        public string? UpdatedBy { get; set; }

        public virtual JobSchedule? JobSchedule { get; set; }
    }
}
