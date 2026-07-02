window.getGeolocation = () => new Promise((resolve, reject) => {
    if (!navigator.geolocation)
        return reject("Géolocalisation non supportée par ce navigateur.");
    navigator.geolocation.getCurrentPosition(
        p => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude }),
        e => reject(e.message),
        { timeout: 10000 }
    );
});

// ── Notifications API ──────────────────────────────────────────────────────
// Local scheduling of night plan reminders. Uses setTimeout while the PWA
// tab is alive. NOT reliable if the user closes the app or if iOS suspends
// the tab overnight — best-effort MVP.

const NOTIFICATION_TIMEOUT_IDS_KEY = '__coolsleep_notification_timeouts';

if (!window[NOTIFICATION_TIMEOUT_IDS_KEY]) {
    window[NOTIFICATION_TIMEOUT_IDS_KEY] = [];
}

window.requestNotificationPermission = async () => {
    if (!('Notification' in window)) return 'unsupported';
    if (Notification.permission === 'granted') return 'granted';
    if (Notification.permission === 'denied') return 'denied';
    try {
        return await Notification.requestPermission();
    } catch (err) {
        console.warn('[Notifications] Permission request failed:', err);
        return 'denied';
    }
};

window.getNotificationPermission = () => {
    if (!('Notification' in window)) return 'unsupported';
    return Notification.permission;
};

window.cancelScheduledNotifications = () => {
    const ids = window[NOTIFICATION_TIMEOUT_IDS_KEY] || [];
    ids.forEach(id => clearTimeout(id));
    window[NOTIFICATION_TIMEOUT_IDS_KEY] = [];
    console.log(`[Notifications] Cancelled ${ids.length} scheduled notifications`);
};

// Schedule notifications. Each action: { hour: 0-23, label: string, detail: string }.
// Returns the number of notifications actually scheduled.
window.scheduleNightPlanNotifications = (actions) => {
    if (!('Notification' in window) || Notification.permission !== 'granted') {
        console.warn('[Notifications] Cannot schedule: permission not granted');
        return 0;
    }

    window.cancelScheduledNotifications();

    const now = new Date();
    let scheduled = 0;

    actions.forEach(action => {
        const target = new Date(now);
        target.setHours(action.hour, 0, 0, 0);

        // If action hour is earlier or equal to now, it means tomorrow (next occurrence).
        if (target.getTime() <= now.getTime()) {
            target.setDate(target.getDate() + 1);
        }

        const delayMs = target.getTime() - now.getTime();
        if (delayMs > 24 * 60 * 60 * 1000) return; // safety: skip if > 24h away

        const timeoutId = setTimeout(() => {
            try {
                new Notification(action.label, {
                    body: action.detail,
                    tag: `coolsleep-action-${action.hour}`,
                    requireInteraction: false
                });
                console.log(`[Notifications] Fired: ${action.hour}h - ${action.label}`);
            } catch (err) {
                console.warn('[Notifications] Failed to show notification:', err);
            }
        }, delayMs);

        window[NOTIFICATION_TIMEOUT_IDS_KEY].push(timeoutId);
        scheduled++;
        console.log(`[Notifications] Scheduled "${action.label}" at ${target.toLocaleString()} (in ${Math.round(delayMs / 60000)} min)`);
    });

    return scheduled;
};
