// Service worker with cache versioning to force updates on deployment
// Version is read from version.json - update that file to bump cache
let CACHE_VERSION = 'coolsleep-v1.0.0';

const CACHE_URLS = [
    '/',
    '/index.html',
    '/app.bundle.js',
    '/app.bundle.css',
    '/manifest.json'
];

// Fetch version from version.json on install
async function getVersion() {
    try {
        const response = await fetch('/version.json');
        const data = await response.json();
        return `coolsleep-v${data.version}`;
    } catch (err) {
        console.warn('[SW] Failed to fetch version.json, using fallback:', err);
        return CACHE_VERSION;
    }
}

self.addEventListener('install', event => {
    event.waitUntil(
        getVersion().then(version => {
            CACHE_VERSION = version;
            console.log('[SW] Installing service worker, cache version:', CACHE_VERSION);
            return caches.open(CACHE_VERSION).then(cache => {
                console.log('[SW] Caching essential assets');
                return cache.addAll(CACHE_URLS).catch(err => {
                    console.warn('[SW] Cache addAll failed (some assets may not exist yet):', err);
                });
            });
        }).then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', event => {
    console.log('[SW] Activating service worker');
    event.waitUntil(
        caches.keys().then(cacheNames => {
            return Promise.all(
                cacheNames.map(cacheName => {
                    if (cacheName !== CACHE_VERSION) {
                        console.log('[SW] Deleting old cache:', cacheName);
                        return caches.delete(cacheName);
                    }
                })
            );
        }).then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', event => {
    // Skip non-GET requests and cross-origin requests
    if (event.request.method !== 'GET' || !event.request.url.startsWith(self.location.origin)) {
        return;
    }

    event.respondWith(
        caches.match(event.request).then(response => {
            if (response) {
                console.log('[SW] Serving from cache:', event.request.url);
                return response;
            }
            return fetch(event.request).then(response => {
                // Cache successful responses
                if (!response || response.status !== 200) {
                    return response;
                }
                const responseToCache = response.clone();
                caches.open(CACHE_VERSION).then(cache => {
                    cache.put(event.request, responseToCache);
                });
                return response;
            }).catch(() => {
                console.log('[SW] Network request failed, no cache available:', event.request.url);
            });
        })
    );
});
