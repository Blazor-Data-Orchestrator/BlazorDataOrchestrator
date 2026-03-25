using System.Collections.Concurrent;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// In-memory store for build errors captured from compilation diagnostics.
/// Thread-safe and lightweight — no external database dependency.
/// </summary>
public class BuildErrorStore
{
    private readonly ConcurrentDictionary<string, List<BuildError>> _errorsByProject = new();
    private readonly ConcurrentQueue<BuildError> _recentErrors = new();
    private readonly int _maxRecentErrors;

    public BuildErrorStore(int maxRecentErrors = 500)
    {
        _maxRecentErrors = maxRecentErrors;
    }

    /// <summary>
    /// Records one or more build errors from a compilation run.
    /// </summary>
    public void RecordErrors(IEnumerable<BuildError> errors)
    {
        foreach (var error in errors)
        {
            _recentErrors.Enqueue(error);

            // Trim the queue if it exceeds the cap
            while (_recentErrors.Count > _maxRecentErrors)
                _recentErrors.TryDequeue(out _);

            var projectErrors = _errorsByProject.GetOrAdd(error.Project, _ => []);
            lock (projectErrors)
            {
                projectErrors.Add(error);

                // Keep only the most recent errors per project
                if (projectErrors.Count > 100)
                    projectErrors.RemoveRange(0, projectErrors.Count - 100);
            }
        }
    }

    /// <summary>
    /// Gets the latest build errors, optionally filtered by project.
    /// </summary>
    public IReadOnlyList<BuildError> GetLatest(int count = 20, string? project = null)
    {
        if (project != null)
        {
            if (_errorsByProject.TryGetValue(project, out var projectErrors))
            {
                lock (projectErrors)
                {
                    return projectErrors.OrderByDescending(e => e.Timestamp).Take(count).ToList();
                }
            }
            return [];
        }

        return _recentErrors.Reverse().Take(count).ToList();
    }

    /// <summary>
    /// Gets the latest errors within a time range.
    /// </summary>
    public IReadOnlyList<BuildError> GetByTimeRange(DateTimeOffset from, DateTimeOffset to, string? project = null)
    {
        var source = project != null && _errorsByProject.TryGetValue(project, out var projectErrors)
            ? projectErrors.AsEnumerable()
            : _recentErrors.AsEnumerable();

        return source
            .Where(e => e.Timestamp >= from && e.Timestamp <= to)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Clears all stored errors.
    /// </summary>
    public void Clear()
    {
        _errorsByProject.Clear();
        while (_recentErrors.TryDequeue(out _)) { }
    }
}
