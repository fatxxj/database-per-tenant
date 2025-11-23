import http from 'k6/http';
import { check, sleep, fail } from 'k6';
import { Rate, Trend } from 'k6/metrics';

// Custom metrics
const errorRate = new Rate('errors');
const tenantBReadLatency = new Trend('tenant_b_read_latency');

// Configuration
export const options = {
  stages: [
    { duration: '30s', target: 10 },
    { duration: '2m', target: 10 },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'], 
    errors: ['rate<0.1'],             
    'tenant_b_read_latency': ['p(95)<300'], 
  },
};

// Test data
const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const TENANT_A_NAME = 'load-test-tenant-a';
const TENANT_B_NAME = 'load-test-tenant-b';
let tenantAToken = '';
let tenantBToken = '';
let tenantAId = '';
let tenantBId = '';

// Helper function to find tenant by name
function findTenantByName(tenants, name) {
  return tenants.find(tenant => tenant.name === name);
}

// Setup function
export function setup() {
  console.log('Setting up test data...');
  
  // get all existing tenants
  const listTenantsResponse = http.get(`${BASE_URL}/tenants`);
  if (listTenantsResponse.status !== 200) {
    fail(`Failed to list tenants: status ${listTenantsResponse.status} body=${listTenantsResponse.body}`);
  }
  
  let tenants = [];
  try {
    tenants = JSON.parse(listTenantsResponse.body);
  } catch (e) {
    fail(`Failed to parse tenants list response: ${listTenantsResponse.body}`);
  }
  
  // Check if tenant A exists
  let existingTenantA = findTenantByName(tenants, TENANT_A_NAME);
  if (existingTenantA) {
    tenantAId = existingTenantA.id; // Changed from tenantId to id
    console.log(`Using existing tenant A: ${tenantAId}`);
  } else {
    // Create tenant A
    const tenantAResponse = http.post(`${BASE_URL}/tenants`, JSON.stringify({
      name: TENANT_A_NAME
    }), {
      headers: { 'Content-Type': 'application/json' },
    });
    
    if (tenantAResponse.status === 409) {
      // Tenant already exists
      console.log(`Tenant A creation returned 409 (already exists), searching for existing tenant...`);
      const retryListResponse = http.get(`${BASE_URL}/tenants`);
      if (retryListResponse.status === 200) {
        try {
          const retryTenants = JSON.parse(retryListResponse.body);
          const retryTenantA = findTenantByName(retryTenants, TENANT_A_NAME);
          if (retryTenantA) {
            tenantAId = retryTenantA.id; // Changed from tenantId to id
            console.log(`Found existing tenant A after retry: ${tenantAId}`);
          } else {
            fail(`Tenant A creation failed with 409 but not found in tenant list`);
          }
        } catch (e) {
          fail(`Failed to parse retry tenants list response: ${retryListResponse.body}`);
        }
      } else {
        fail(`Failed to retry list tenants: status ${retryListResponse.status}`);
      }
    } else if (tenantAResponse.status >= 200 && tenantAResponse.status < 300) {
      try {
        const tenantAData = JSON.parse(tenantAResponse.body);
        tenantAId = tenantAData.tenantId;
        console.log(`Created tenant A: ${tenantAId}`);
      } catch (e) {
        fail(`Failed to parse tenant A creation response: ${tenantAResponse.body}`);
      }
    } else {
      fail(`Failed to create tenant A: status ${tenantAResponse.status} body=${tenantAResponse.body}`);
    }
  }
  
  // Check if tenant B exists
  let existingTenantB = findTenantByName(tenants, TENANT_B_NAME);
  if (existingTenantB) {
    tenantBId = existingTenantB.id; // Changed from tenantId to id
    console.log(`Using existing tenant B: ${tenantBId}`);
  } else {
    // Create tenant B
    const tenantBResponse = http.post(`${BASE_URL}/tenants`, JSON.stringify({
      name: TENANT_B_NAME
    }), {
      headers: { 'Content-Type': 'application/json' },
    });
    
    if (tenantBResponse.status === 409) {
      // Tenant already exists
      console.log(`Tenant B creation returned 409 (already exists), searching for existing tenant...`);
      const retryListResponse = http.get(`${BASE_URL}/tenants`);
      if (retryListResponse.status === 200) {
        try {
          const retryTenants = JSON.parse(retryListResponse.body);
          const retryTenantB = findTenantByName(retryTenants, TENANT_B_NAME);
          if (retryTenantB) {
            tenantBId = retryTenantB.id; // Changed from tenantId to id
            console.log(`Found existing tenant B after retry: ${tenantBId}`);
          } else {
            fail(`Tenant B creation failed with 409 but not found in tenant list`);
          }
        } catch (e) {
          fail(`Failed to parse retry tenants list response: ${retryListResponse.body}`);
        }
      } else {
        fail(`Failed to retry list tenants: status ${retryListResponse.status}`);
      }
    } else if (tenantBResponse.status >= 200 && tenantBResponse.status < 300) {
      try {
        const tenantBData = JSON.parse(tenantBResponse.body);
        tenantBId = tenantBData.tenantId;
        console.log(`Created tenant B: ${tenantBId}`);
      } catch (e) {
        fail(`Failed to parse tenant B creation response: ${tenantBResponse.body}`);
      }
    } else {
      fail(`Failed to create tenant B: status ${tenantBResponse.status} body=${tenantBResponse.body}`);
    }
  }
  
  // Get tokens for both tenants
  const tokenAResponse = http.post(`${BASE_URL}/auth/dev-token`, JSON.stringify({
    tenantId: tenantAId
  }), {
    headers: { 'Content-Type': 'application/json' },
  });
  if (tokenAResponse.status >= 200 && tokenAResponse.status < 300) {
    try {
      const tokenData = JSON.parse(tokenAResponse.body);
      tenantAToken = tokenData.token;
    } catch (e) {
      fail(`Failed to parse token A response: ${tokenAResponse.body}`);
    }
  } else {
    fail(`Failed to get token for tenant A: status ${tokenAResponse.status} body=${tokenAResponse.body}`);
  }
  
  const tokenBResponse = http.post(`${BASE_URL}/auth/dev-token`, JSON.stringify({
    tenantId: tenantBId
  }), {
    headers: { 'Content-Type': 'application/json' },
  });
  if (tokenBResponse.status >= 200 && tokenBResponse.status < 300) {
    try {
      const tokenData = JSON.parse(tokenBResponse.body);
      tenantBToken = tokenData.token;
    } catch (e) {
      fail(`Failed to parse token B response: ${tokenBResponse.body}`);
    }
  } else {
    fail(`Failed to get token for tenant B: status ${tokenBResponse.status} body=${tokenBResponse.body}`);
  }

  if (!tenantAId || !tenantBId || !tenantAToken || !tenantBToken) {
    fail(`Setup failed. tenantAId=${tenantAId} tenantBId=${tenantBId} tenantAToken=${tenantAToken ? 'set' : 'missing'} tenantBToken=${tenantBToken ? 'set' : 'missing'}`);
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
      'tenant A order creation successful': (r) => r.status === 201 || r.status === 200,
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
      'tenant A event creation successful': (r) => r.status === 201 || r.status === 200,
    });
    
    errorRate.add(!((orderResponse.status === 201 || orderResponse.status === 200) && (eventResponse.status === 201 || eventResponse.status === 200)));
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
