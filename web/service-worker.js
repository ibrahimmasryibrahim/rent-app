const CACHE_NAME = "rent-app-shell-v1";
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

// Network-first for API/socket traffic (never serve stale financial data as if
// it were fresh); cache-first for the static app shell so the app still opens
// (with a clear "offline" indicator handled in app.js) when there's no signal.
self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);
  if (url.pathname.startsWith("/auth") || url.pathname.startsWith("/groups") || url.pathname.startsWith("/notifications") || url.pathname.startsWith("/socket.io")) {
    return; // let it hit the network directly, no SW interception
  }
  event.respondWith(
    caches.match(event.request).then((cached) => cached || fetch(event.request))
  );
});
