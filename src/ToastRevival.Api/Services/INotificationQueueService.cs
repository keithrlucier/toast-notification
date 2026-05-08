namespace ToastRevival.Api.Services;

public interface INotificationQueueService
{
    void Enqueue(Guid notificationId);
}
