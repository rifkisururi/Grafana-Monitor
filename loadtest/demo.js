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
    slow_report: {
      executor: 'constant-arrival-rate', startTime: '45s', duration: '105s', rate: 2, timeUnit: '1s',
      preAllocatedVUs: 20, maxVUs: 50, exec: 'heavy', tags: { endpoint: 'heavy' },
    },
    health_control: {
      executor: 'constant-arrival-rate', startTime: '0s', duration: '210s', rate: 1, timeUnit: '1s',
      preAllocatedVUs: 5, maxVUs: 20, exec: 'health', tags: { endpoint: 'health' },
    },
  },
  thresholds: {},
};

function request(path, endpoint) {
  const response = http.get(`${baseUrl}${path}`, {
    tags: { endpoint }, timeout: endpoint === 'heavy' ? '25s' : '10s',
  });
  responseCodes.add(1, { endpoint, status: String(response.status) });
  failed.add(response.status < 200 || response.status >= 400, { endpoint });
}

export function products() { request('/api/products', 'products'); }
export function orders() { request('/api/orders', 'orders'); }
export function heavy() { request('/api/reports/heavy', 'heavy'); }
export function health() { request('/health/live', 'health'); }

export function handleSummary(data) {
  return {
    '/output/k6-summary.json': JSON.stringify(data, null, 2),
    stdout: `\nRingkasan k6 tersimpan. dropped_iterations=${data.metrics.dropped_iterations?.values?.count ?? 0}\n`,
  };
}
