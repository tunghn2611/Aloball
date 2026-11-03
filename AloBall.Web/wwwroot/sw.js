// Safe Service Worker - Chỉ cache static assets an toàn, không cache trang HTML động hay API
const CACHE_NAME = 'aloball-football-v4';
const STATIC_ASSETS = [
  '/manifest.json',
  '/favicon.ico',
  '/css/arena.css',
  '/js/arena.js',
  '/lib/bootstrap/dist/css/bootstrap.min.css',
  '/lib/bootstrap/dist/js/bootstrap.bundle.min.js'
];

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME).then(cache => {
      return cache.addAll(STATIC_ASSETS);
    })
  );
  self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys => {
      return Promise.all(
        keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key))
      );
    })
  );
  self.clients.claim();
});

self.addEventListener('fetch', event => {
  // Chỉ phục vụ GET requests cho static assets
  if (event.request.method !== 'GET') return;
  const url = new URL(event.request.url);

  // Không cache API, Account, hoặc bất kỳ trang HTML navigation nào
  if (url.pathname.startsWith('/Account') || 
      url.pathname.startsWith('/api') || 
      url.pathname.startsWith('/Sheets') || 
      event.request.mode === 'navigate' ||
      event.request.destination === 'document') {
    return;
  }

  event.respondWith(
    caches.match(event.request).then(response => {
      return response || fetch(event.request);
    })
  );
});
