// Skenario SUDAH DIPERBAIKI: report memanggil /api/reports/normal (COUNT(*) tanpa
// pg_sleep) — profil beban identik dengan heavy.js, hanya endpoint report yang berbeda.
// Bandingkan /output/k6-normal.json dengan /output/k6-heavy.json sebagai bukti before/after.
// Jalankan: docker compose run --rm k6 run /scripts/normal.js
import http from 'k6/http';
import { Counter, Rate } from 'k6/metrics';

const baseUrl = __ENV.BASE_URL || 'http://api:8080';
const responseCodes = new Counter('response_codes');
const failed = new Rate('request_failed');

export const options = {
  scenarios: {
    fast_products: {
      executor: 'constant-arrival-rate', startTime: '0s', duration: '210s', rate: 2, timeUnit: '1s',
      preAllocatedVUs: 10, maxVUs: 50, exec: 'products', tags: { endpoint: 'products' },
    },
    fast_orders: {
      executor: 'constant-arrival-rate', startTime: '0s', duration: '210s', rate: 2, timeUnit: '1s',
      preAllocatedVUs: 10, maxVUs: 50, exec: 'orders', tags: { endpoint: 'orders' },
    },
    fixed_report: {
      executor: 'constant-arrival-rate', startTime: '45s', duration: '105s', rate: 2, timeUnit: '1s',
      preAllocatedVUs: 20, maxVUs: 50, exec: 'report', tags: { endpoint: 'normal' },
    },
    health_control: {
      executor: 'constant-arrival-rate', startTime: '0s', duration: '210s', rate: 1, timeUnit: '1s',
      preAllocatedVUs: 5, maxVUs: 20, exec: 'health', tags: { endpoint: 'health' },
    },
  },
  // Threshold ringan sebagai bukti skenario sehat setelah perbaikan:
  // rasio gagal (status >= 400 / timeout) harus tetap di bawah 1%.
  thresholds: {
    'http_req_failed': ['rate<0.01'],
  },
};

function request(path, endpoint, timeout) {
  const response = http.get(`${baseUrl}${path}`, { tags: { endpoint }, timeout });
  responseCodes.add(1, { endpoint, status: String(response.status) });
  failed.add(response.status < 200 || response.status >= 400, { endpoint });
}

export function products() { request('/api/products', 'products', '10s'); }
export function orders() { request('/api/orders', 'orders', '10s'); }
export function report() { request('/api/reports/normal', 'normal', '10s'); }
export function health() { request('/health/live', 'health', '10s'); }

export function handleSummary(data) {
  return {
    '/output/k6-normal.json': JSON.stringify(data, null, 2),
    stdout: `\nRingkasan k6 (normal/sudah diperbaiki) tersimpan. dropped_iterations=${data.metrics.dropped_iterations?.values?.count ?? 0}\n`,
  };
}
