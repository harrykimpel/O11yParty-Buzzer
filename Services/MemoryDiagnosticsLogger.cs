namespace O11yPartyBuzzer.Services;

/// <summary>
/// Provides helper methods to log GC and process memory statistics for diagnosing
/// memory pressure and garbage-collection-related performance issues.
/// </summary>
public static class MemoryDiagnosticsLogger
{
    /// <summary>
    /// Logs a snapshot of current GC and process memory metrics at the Information level.
    /// Call this from performance-sensitive paths to correlate memory pressure with slow operations.
    /// </summary>
    public static void LogMemoryStats(ILogger logger, string operationContext)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var gcInfo = GC.GetGCMemoryInfo();
        var totalAllocated = GC.GetTotalAllocatedBytes(precise: false);
        var totalMemory = GC.GetTotalMemory(forceFullCollection: false);
        var workingSet = Environment.WorkingSet;

        logger.LogInformation(
            "MemoryDiagnostics [{Context}]: WorkingSet={WorkingSetMb:F1} MB, " +
            "GcHeap={GcHeapMb:F1} MB, TotalAllocated={TotalAllocatedMb:F1} MB, " +
            "HeapSizeBytes={HeapSizeBytes}, Gen0={Gen0}, Gen1={Gen1}, Gen2={Gen2}",
            operationContext,
            workingSet / 1_048_576.0,
            totalMemory / 1_048_576.0,
            totalAllocated / 1_048_576.0,
            gcInfo.HeapSizeBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }

    /// <summary>
    /// Logs a warning if memory metrics indicate potential GC pressure.
    /// Threshold: working set above <paramref name="workingSetWarningMb"/> MB.
    /// </summary>
    public static void WarnIfMemoryPressure(ILogger logger, string operationContext, int workingSetWarningMb = 512)
    {
        var workingSet = Environment.WorkingSet;
        var workingSetMb = workingSet / 1_048_576.0;

        if (workingSetMb > workingSetWarningMb)
        {
            logger.LogWarning(
                "MemoryPressure [{Context}]: WorkingSet={WorkingSetMb:F1} MB exceeds threshold of {ThresholdMb} MB. " +
                "Gen0={Gen0}, Gen1={Gen1}, Gen2={Gen2}",
                operationContext,
                workingSetMb,
                workingSetWarningMb,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2));
        }
    }
}
