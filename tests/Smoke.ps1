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
$normal = Invoke-RestMethod "$baseUrl/api/reports/normal" -TimeoutSec 15
$timer.Stop()
if ($normal.orderCount -lt 1000) { throw 'Normal report returned unexpected order count.' }
if ($normal.mode -ne 'normal') { throw 'Normal report did not report mode=normal.' }

$timer = [Diagnostics.Stopwatch]::StartNew()
$report = Invoke-RestMethod "$baseUrl/api/reports/heavy" -TimeoutSec 25
$timer.Stop()
if ($report.orderCount -lt 1000) { throw 'Report order count is lower than expected seed.' }
if ($timer.Elapsed.TotalSeconds -lt 2.5) { throw 'Slow report returned too quickly; check DEMO_REPORT_MODE.' }

$memory = Invoke-RestMethod "$baseUrl/api/reports/heavy-memory" -TimeoutSec 30
$cpu = Invoke-RestMethod "$baseUrl/api/reports/heavy-cpu" -TimeoutSec 30
$both = Invoke-RestMethod "$baseUrl/api/reports/heavy-both" -TimeoutSec 30
if ($memory.workload -ne 'memory' -or $memory.allocatedMegabytes -lt 1 -or $memory.durationSeconds -lt 1) { throw 'Heavy memory endpoint did not report its bounded allocation and duration.' }
if ($cpu.workload -ne 'cpu' -or $cpu.allocatedMegabytes -ne 0 -or $cpu.durationSeconds -lt 1) { throw 'Heavy CPU endpoint returned unexpected workload metadata.' }
if ($both.workload -ne 'memory+cpu' -or $both.allocatedMegabytes -lt 1 -or $both.durationSeconds -lt 1) { throw 'Combined workload endpoint returned unexpected metadata.' }

Write-Host "Smoke checks passed: products=$($products.Count), orders=$($orders.Count), heavySeconds=$([math]::Round($timer.Elapsed.TotalSeconds, 2)), stressMemory=$($memory.allocatedMegabytes)MB, stressCpu=$($cpu.durationSeconds)s, combined=$($both.workload)"; if ($both) { Write-Host 'Resource stress checks passed.' } else { throw 'Resource stress checks failed.' }
