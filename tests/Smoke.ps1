$ErrorActionPreference = 'Stop'
$baseUrl = if ($env:BASE_URL) { $env:BASE_URL } else { 'http://localhost:8080' }

$products = Invoke-RestMethod "$baseUrl/api/products"
$orders = Invoke-RestMethod "$baseUrl/api/orders"
$health = Invoke-RestMethod "$baseUrl/health/live"
$metrics = Invoke-WebRequest "$baseUrl/metrics"

if ($products.Count -lt 1) { throw 'Products endpoint returned no rows.' }
if ($orders.Count -lt 1) { throw 'Orders endpoint returned no rows.' }
if ($health.status -ne 'ok') { throw 'Health endpoint did not return status=ok.' }
if ($metrics.StatusCode -ne 200 -or $metrics.Content -notmatch 'demo_db_pool') { throw 'Prometheus metrics missing.' }

$timer = [Diagnostics.Stopwatch]::StartNew()
$report = Invoke-RestMethod "$baseUrl/api/reports/heavy" -TimeoutSec 25
$timer.Stop()
if ($report.orderCount -lt 1000) { throw 'Report order count is lower than expected seed.' }
if ($timer.Elapsed.TotalSeconds -lt 2.5) { throw 'Slow report returned too quickly; check DEMO_REPORT_MODE.' }

Write-Host "Smoke checks passed: products=$($products.Count), orders=$($orders.Count), heavySeconds=$([math]::Round($timer.Elapsed.TotalSeconds, 2))"
