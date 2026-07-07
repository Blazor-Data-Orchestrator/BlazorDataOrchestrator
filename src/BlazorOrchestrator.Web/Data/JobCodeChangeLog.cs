#nullable disable
using System;
using System.ComponentModel.DataAnnotations;

namespace BlazorOrchestrator.Web.Data.Data;

public partial class JobCodeChangeLog
{
    [Key]
    public int Id { get; set; }

    public int JobId { get; set; }

    public string UserId { get; set; }

    public string UserName { get; set; }

    public string ChangeType { get; set; }

    public string FileName { get; set; }

    public string Language { get; set; }

    public string SnapshotRowKey { get; set; }

    public string Summary { get; set; }

    public int LinesAdded { get; set; }

    public int LinesRemoved { get; set; }

    public DateTime CreatedDate { get; set; }
}
