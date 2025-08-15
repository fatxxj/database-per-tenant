import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

// Custom metrics
const errorRate = new Rate('errors');
const tenantBReadLatency = new Trend('tenant_b_read_latency');

// Configuration
export const options = {
  stages: [
    { duration: '30s', target: 10 }, // Ramp up
    { duration: '2m', target: 10 },  // Steady load
    { duration: '30s', target: 0 },  // Ramp down
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'], // 95% of requests should be below 500ms
    errors: ['rate<0.1'],             // Error rate should be less than 10%
    'tenant_b_read_latency': ['p(95)<300'], // Tenant B reads should be fast
  },
};

// Test data
const BASE_URL = 'http://localhost:8080';
let tenantAToken = '';
let tenantBToken = '';
let tenantAId = '';
let tenantBId = '';

// Setup function - runs once before the test
export function setup() {
  console.log('Setting up test data...');
  
  // Create tenant A
  const tenantAResponse = http.post(`${BASE_URL}/tenants`, JSON.stringify({
    name: 'load-test-tenant-a'
  }), {
    headers: { 'Content-Type': 'application/json' },
  });
  
  if (tenantAResponse.status === 200) {
    const tenantAData = JSON.parse(tenantAResponse.body);
    tenantAId = tenantAData.tenantId;
    console.log(`Created tenant A: ${tenantAId}`);
  }
  
  // Create tenant B
  const tenantBResponse = http.post(`${BASE_URL}/tenants`, JSON.stringify({
    name: 'load-test-tenant-b'
  }), {
    headers: { 'Content-Type': 'application/json' },
  });
  
  if (tenantBResponse.status === 200) {
    const tenantBData = JSON.parse(tenantBResponse.body);
    tenantBId = tenantBData.tenantId;
    console.log(`Created tenant B: ${tenantBId}`);
  }
  
  // Get tokens for both tenants
  const tokenAResponse = http.post(`${BASE_URL}/auth/dev-token`, JSON.stringify({
    tenantId: tenantAId
  }), {
    headers: { 'Content-Type': 'application/json' },
  });
  
  if (tokenAResponse.status === 200) {
    const tokenData = JSON.parse(tokenAResponse.body);
    tenantAToken = tokenData.token;
  }
  
  const tokenBResponse = http.post(`${BASE_URL}/auth/dev-token`, JSON.stringify({
    tenantId: tenantBId
  }), {
    headers: { 'Content-Type': 'application/json' },
  });
  
  if (tokenBResponse.status === 200) {
    const tokenData = JSON.parse(tokenBResponse.body);
    tenantBToken = tokenData.token;
  }
  
  // Create some initial data for tenant A
  for (let i = 0; i < 10; i++) {
    http.post(`${BASE_URL}/api/orders`, JSON.stringify({
      code: `ORD-A-${i}`,
      amount: 100 + i
    }), {
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${tenantAToken}`,
        'X-Tenant-Id': tenantAId
      },
    });
    
    http.post(`${BASE_URL}/api/events`, JSON.stringify({
      type: 'order.created',
      payload: { orderId: i, amount: 100 + i }
    }), {
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${tenantAToken}`,
        'X-Tenant-Id': tenantAId
      },
    });
  }
  
  // Create some initial data for tenant B
  for (let i = 0; i < 5; i++) {
    http.post(`${BASE_URL}/api/orders`, JSON.stringify({
      code: `ORD-B-${i}`,
      amount: 200 + i
    }), {
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${tenantBToken}`,
        'X-Tenant-Id': tenantBId
      },
    });
    
    http.post(`${BASE_URL}/api/events`, JSON.stringify({
      type: 'order.created',
      payload: { orderId: i, amount: 200 + i }
    }), {
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${tenantBToken}`,
        'X-Tenant-Id': tenantBId
      },
    });
  }
  
  console.log('Setup complete');
  return { tenantAId, tenantBId, tenantAToken, tenantBToken };
}

// Main test function
export default function(data) {
  const { tenantAId, tenantBId, tenantAToken, tenantBToken } = data;
  
  // Scenario 1: Heavy write load on tenant A
  const writeLoad = Math.random() < 0.7; // 70% of requests are writes to tenant A
  
  if (writeLoad) {
    // Heavy write operations on tenant A
    const orderResponse = http.post(`${BASE_URL}/api/orders`, JSON.stringify({
      code: `ORD-A-${Date.now()}`,
      amount: Math.random() * 1000
    }), {
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${tenantAToken}`,
        'X-Tenant-Id': tenantAId
      },
    });
    
    check(orderResponse, {
      'tenant A order creation successful': (r) => r.status === 201,
    });
    
    const eventResponse = http.post(`${BASE_URL}/api/events`, JSON.stringify({
      type: 'order.created',
      payload: { 
        orderId: Date.now(),
        amount: Math.random() * 1000,
        timestamp: new Date().toISOString()
      }
    }), {
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${tenantAToken}`,
        'X-Tenant-Id': tenantAId
      },
    });
    
    check(eventResponse, {
      'tenant A event creation successful': (r) => r.status === 201,
    });
    
    errorRate.add(orderResponse.status !== 201 || eventResponse.status !== 201);
  } else {
    // Light read operations on tenant B (should remain fast despite tenant A load)
    const startTime = Date.now();
    
    const ordersResponse = http.get(`${BASE_URL}/api/orders`, {
      headers: {
        'Authorization': `Bearer ${tenantBToken}`,
        'X-Tenant-Id': tenantBId
      },
    });
    
    const eventsResponse = http.get(`${BASE_URL}/api/events`, {
      headers: {
        'Authorization': `Bearer ${tenantBToken}`,
        'X-Tenant-Id': tenantBId
      },
    });
    
    const endTime = Date.now();
    const latency = endTime - startTime;
    tenantBReadLatency.add(latency);
    
    check(ordersResponse, {
      'tenant B orders read successful': (r) => r.status === 200,
    });
    
    check(eventsResponse, {
      'tenant B events read successful': (r) => r.status === 200,
    });
    
    errorRate.add(ordersResponse.status !== 200 || eventsResponse.status !== 200);
  }
  
  sleep(0.1); // Small delay between requests
}

// Teardown function - runs once after the test
export function teardown(data) {
  console.log('Cleaning up test data...');
  
  // In a real scenario, you might want to clean up the test tenants
  // For now, we'll just log the completion
  console.log('Test completed');
}

// Handle test completion
export function handleSummary(data) {
  console.log('Test Summary:');
  console.log(`Total requests: ${data.metrics.http_reqs.values.count}`);
  console.log(`Error rate: ${(data.metrics.errors.values.rate * 100).toFixed(2)}%`);
  console.log(`Average response time: ${data.metrics.http_req_duration.values.avg.toFixed(2)}ms`);
  console.log(`95th percentile response time: ${data.metrics.http_req_duration.values['p(95)'].toFixed(2)}ms`);
  
  if (data.metrics['tenant_b_read_latency']) {
    console.log(`Tenant B read latency - 95th percentile: ${data.metrics['tenant_b_read_latency'].values['p(95)'].toFixed(2)}ms`);
  }
  
  return {
    'stdout': JSON.stringify(data, null, 2),
  };
}
