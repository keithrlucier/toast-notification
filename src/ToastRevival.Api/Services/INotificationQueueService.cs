namespace ToastRevival.Api.Services;

public interface INotificationQueueService
{
    /// <summary>
    /// Enqueues a notification for dispatch. Returns false when the channel is full
    /// (capacity 10,000); callers should return 503 to the sender in that case.
    /// </summary>
    bool Enqueue(Guid notificationId);

    /// <summary>
    /// Current depth of the in-memory dispatch channel. Tracked via
    /// Interlocked counter mirroring the channel; producer increments
    /// after a successful TryWrite, consumer decrements after a successful
    /// read. Surfaced for the health endpoint so external probes can spot
    /// a hung consumer (queue climbing without producer pressure
    /// increasing). Steady-state production queue should be ~zero except
    /// in burst windows.
    /// </summary>
    int QueueDepth { get; }
}
