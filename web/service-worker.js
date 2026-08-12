const CACHE_NAME = "rent-app-shell-v2";
const APP_SHELL = [
  "/",
  "/index.html",
  "/css/styles.css",
  "/js/api.js",
  "/js/app.js",
  "/manifest.json"
];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(CACHE_NAME).then((cache) => cache.addAll(APP_SHELL)));
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== CACHE_NAME).map((k) => caches.delete(k))))
  );
  self.clients.claim();
});

// Network-first for everything: a stale app shell is worse than a slightly
// slower load, since it can silently hide code changes (auth flow, screens)
// behind a cached copy. The cache only exists as an offline fallback, and API
// calls always go straight to the network (never cached — financial data must
// never appear "fresh" when it isn't).
self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);
  if (url.pathname.startsWith("/auth") || url.pathname.startsWith("/groups") || url.pathname.startsWith("/notifications") || url.pathname.startsWith("/socket.io")) {
    return; // let it hit the network directly, no SW interception
  }
  event.respondWith(
    fetch(event.request)
      .then((res) => {
        const copy = res.clone();
        caches.open(CACHE_NAME).then((cache) => cache.put(event.request, copy));
        return res;
      })
      .catch(() => caches.match(event.request))
  );
});
