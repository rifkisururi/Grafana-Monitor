# Demo Observability ASP.NET Core + PostgreSQL

Demo lokal untuk mengamati **Npgsql connection-pool contention**. Endpoint laporan menahan koneksi bersama selama `pg_sleep`; products/orders sederhana kemudian dapat menunggu koneksi. Prometheus mengumpulkan metrik dan Grafana dashboard diprovision otomatis. `pg_sleep` hanya simulasi kerja database lambat, bukan contoh optimasi SQL produksi.

## Versi dependency dan tools

- .NET target 10.0; Docker SDK/runtime memakai feature tag `10.0` (patch upstream dapat bergerak). SDK lokal: `10.0.401`.
- Npgsql `10.0.0`.
- OpenTelemetry.Extensions.Hosting `1.15.0`; Prometheus ASP.NET exporter `1.15.0-beta.1`.
- PostgreSQL `17.6-alpine`, Prometheus `v3.5.0`, Grafana `12.1.1`, k6 `0.54.0`.

Build lokal berhasil, tetapi dependency transitif `OpenTelemetry.Api 1.15.0` memunculkan advisory moderat NU1902, GHSA-g94r-2vxg-569j. Jangan abaikan advisory: cek versi patch yang memperbaikinya, update dependency, lalu ulangi pemeriksaan keamanan sebelum penggunaan. Docker tags dipin ke versi eksplisit, tetapi tag image dapat diperbarui patch upstream; untuk reproduksibilitas ketat pin digest.

## Prasyarat dan startup lokal

Docker Engine/Desktop dengan Linux containers, Docker Compose v2, dan akses jaringan untuk menarik image/paket saat build. Belum ada klaim estimasi resource sebelum diukur.

```sh
cp .env.example .env
# Edit .env: gunakan password unik untuk demo lokal; jangan gunakan nilai demo di production.
docker compose config
docker compose up --build -d
docker compose ps
```

Port dipublish hanya pada loopback `127.0.0.1`:

- API: http://localhost:8080
- Prometheus: http://localhost:9090
- Grafana: http://localhost:3000 (credential dari `.env`)

Jika port bentrok, ubah `API_PORT`, `PROMETHEUS_PORT`, atau `GRAFANA_PORT` di `.env`. Jangan expose layanan demo ini ke jaringan publik. Compose membuat bridge network `grafana-monitor-demo`; untuk memakai network external `rifki`, set `DOCKER_NETWORK=rifki` dan pastikan network itu sudah ada serta layanan diizinkan terhubung.

### Memakai Neon secara opsional

Secara default Compose menggunakan PostgreSQL lokal agar demo observability tetap terisolasi. Untuk menjalankan API terhadap Neon, set environment `DATABASE_URL` ke URI PostgreSQL Neon dan gunakan service API dengan konfigurasi deployment yang sesuai. Jangan taruh kredensial di `appsettings.json` atau commit `.env`. Parser mengonversi URI ke connection string Npgsql, mewajibkan TLS (`sslmode=require`), mempertahankan pooling, dan mendukung `channel_binding`. Karena URL sebelumnya telah dibagikan di percakapan, rotasi password Neon sebelum digunakan kembali.

**Jangan arahkan load test atau endpoint heavy ke Neon/produksi.** Untuk sekadar verifikasi konektivitas pada database yang terotorisasi dan sudah memakai credential baru, set `DATABASE_URL`, stop service PostgreSQL lokal agar tidak tercampur, lalu jalankan hanya query endpoint normal atau tambahkan health check DB yang disetujui. `/api/reports/heavy` menjalankan `pg_sleep`; jangan gunakan terhadap database remote tanpa persetujuan dan batas beban yang jelas.

## Verifikasi layanan

```sh
curl http://localhost:8080/api/products
curl http://localhost:8080/api/orders
curl http://localhost:8080/health/live
curl http://localhost:8080/metrics
```

Products/orders membaca data PostgreSQL yang di-seed. `/api/reports/heavy` pada mode slow memegang lease koneksi selama query `pg_sleep` (default 3 detik), kemudian menjalankan `COUNT(*)` pada koneksi yang sama. Periksa Prometheus **Status → Targets**; `aspnet-api` harus UP. Dashboard **API Incident Lab — PostgreSQL Pool Contention** tersedia otomatis di Grafana, folder Incident Lab.

Custom metrics aplikasi: `demo_db_pool_acquire_seconds`, `demo_db_pool_waiters`, `demo_db_connection_leased`, `demo_db_query_seconds`. Aplikasi mengaktifkan meter HTTP ASP.NET Core dan Npgsql. Nama dan label Prometheus aktual belum diverifikasi karena Docker Engine belum tersedia di sesi implementasi; periksa output `/metrics` lalu cocokkan PromQL/dashboard dengan exposition yang sesungguhnya.

## Demo baseline → incident → recovery

Setelah semua service sehat:

```sh
docker compose run --rm k6 run --summary-export=/output/k6-raw.json /scripts/demo.js
```

BASE_URL default `http://api:8080`. Products (2 iterasi/s), orders (2/s) dan health (1/s) berjalan 210 detik. Heavy (2/s) dimulai pada detik 45, berjalan 105 detik, lalu berhenti. Hasil summary ditulis ke `artifacts/k6-summary.json`, dan `--summary-export` menyimpan JSON tambahan ke `artifacts/k6-raw.json`. Analisis fase baseline 0–45s, incident 45–150s, recovery 150–210s. Periksa request rate, dropped iterations, status/failure, p95 HTTP, waktu acquire, SQL duration, waiter, koneksi pool, dan kontrol health.

Spillover baru terbukti bila kedua endpoint products dan orders memiliki kenaikan terukur, acquire wait/waiters naik sementara SQL masing-masing tetap singkat, pool mencapai kapasitas, health relatif stabil, dan p95 pulih setelah heavy berhenti. Dashboard saja bukan bukti. Query diagnosis DBA saat incident:

```sql
SELECT pid, application_name, state, wait_event_type, wait_event,
       now() - query_start AS running_for, LEFT(query, 120) AS query_preview
FROM pg_stat_activity
WHERE application_name = 'monitor-demo-api'
ORDER BY query_start NULLS LAST;
```

Request yang menunggu sebelum mendapat koneksi belum terlihat di `pg_stat_activity`; gunakan metrik custom waiter/acquisition.

## Eksperimen mitigasi

Mode `fast` melewati `pg_sleep` untuk mensimulasikan pengurangan kerja DB; ini bukan optimasi query nyata. Edit `.env` menjadi `DEMO_REPORT_MODE=fast`, lalu recreate API dan ulangi beban yang sama:

```sh
docker compose up -d --force-recreate api
docker compose run --rm k6 run --summary-export=/output/k6-fast-raw.json /scripts/demo.js
```

Restart memulai proses baru, bukan pemulihan alami. Kembalikan `DEMO_REPORT_MODE=slow` dan recreate API setelah eksperimen. Alternatif capacity mitigation: ubah `DB_POOL_MAX_SIZE=20`, tetap slow, ulangi beban identik. Pool lebih besar dapat mengurangi antrean, tetapi tidak memperbaiki query lambat dan menaikkan potensi jumlah koneksi PostgreSQL; bukan solusi produksi universal.

## Konfigurasi dan troubleshooting

- `DEMO_HEAVY_SLEEP_SECONDS`: bilangan bulat 0–5, default 3; tervalidasi saat startup dan diparameterisasi ke SQL.
- `DEMO_REPORT_MODE`: `slow` (sleep SQL) atau `fast`; perubahan memerlukan recreate API. `appsettings.json` menyediakan default lokal `Demo:*`, dan environment variables Compose mengoverride nilai tersebut.
- `DB_POOL_MAX_SIZE`: kapasitas pool, default 4.
- DB tidak tersambung: periksa `docker compose ps` dan `docker compose logs postgres api`; pastikan konfigurasi `.env` benar. Skrip init hanya berjalan pada volume baru.
- Seed lama: perubahan init script tidak berlaku surut. Simpan evidence sebelum `docker compose down -v`, karena opsi itu menghapus data DB/Prometheus/Grafana.
- `/metrics` kosong: panggil endpoint bisnis, lihat log API, pastikan exporter/meter terdaftar dan target scrape UP.
- Panel `No data` atau PromQL gagal: cocokkan nama metric, suffix histogram, route/status labels, dan format aktual `/metrics`.
- k6 `dropped_iterations`: kalibrasi VU allocation; laporkan jika traffic drop, dan jangan membandingkan run dengan beban berbeda seolah identik.
- Heavy timeout: periksa `Command Timeout` 20 detik, sleep maksimal 5 detik, k6 timeout, dan log API.
- Docker daemon tidak berjalan: mulai Docker Desktop dalam mode Linux containers lalu ulangi.

## Keamanan, batasan, teardown

Semua data dummy tanpa PII. Jangan commit `.env`; gunakan secrets manager di lingkungan lain. Metrik tidak menggunakan request ID/password/raw connection string. Konfigurasi sengaja membuat operasi lambat dan pool kecil; jangan deploy atau expose demo ini ke internet. Satu instance API digunakan agar analisis pool tidak rancu. Demo menunjukkan pool contention, bukan CPU saturation, lock contention, atau optimasi SQL produksi.

```sh
docker compose down       # hapus container/network, pertahankan volume
docker compose down -v    # hapus data DB, Prometheus, Grafana; simpan evidence dahulu
```

Status uji faktual ada di `artifacts/evidence.md`. Tidak ada angka eksperimen yang dicantumkan sebelum benar-benar dikumpulkan.
