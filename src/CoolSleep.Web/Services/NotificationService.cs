namespace CoolSleep.Web.Services;

using CoolSleep.Web.Models;
using Microsoft.JSInterop;

public sealed class NotificationService(IJSRuntime js)
{
    public async Task<string> GetPermissionAsync() =>
        await js.InvokeAsync<string>("getNotificationPermission");

    public async Task<string> RequestPermissionAsync() =>
        await js.InvokeAsync<string>("requestNotificationPermission");

    public async Task<int> ScheduleAsync(IReadOnlyList<NotificationScheduleItem> items) =>
        await js.InvokeAsync<int>("scheduleNightPlanNotifications", items);

    public async Task CancelAsync() =>
        await js.InvokeVoidAsync("cancelScheduledNotifications");
}
