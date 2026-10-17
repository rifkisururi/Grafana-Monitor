# Evidence pengujian

## Yang benar-benar dijalankan

- `dotnet build Grafana-Monitor.csproj --verbosity quiet` setelah pemisahan controller/service/repository/DTO — **berhasil**, 0 error; menghasilkan 2 peringatan NU1902 untuk dependency transitif `OpenTelemetry.Api 1.15.0`, advisory moderat GHSA-g94r-2vxg-569j.
- `docker compose --env-file .env.example config --quiet` — **berhasil**, konfigurasi Compose terurai.
- `dotnet --version` — SDK `10.0.401`.
- `docker --version` dan `docker compose version` — tersedia CLI Docker `27.2.0` dan Compose `v2.29.2-desktop.2`.

## Belum dijalankan

Docker Engine Desktop tidak berjalan di sesi ini: koneksi ke named pipe `dockerDesktopLinuxEngine` gagal karena file tidak ditemukan. Karena itu container, API runtime, PostgreSQL seed, endpoint HTTP, exposition `/metrics`, Prometheus target UP, dashboard/provisioning, k6 tiga fase, serta eksperimen mitigasi **belum diuji**. Tidak ada p95, rate, pool waiter, dropped iteration, atau hasil empiris yang dapat dilaporkan. Acceptance criteria eksperimen belum dinyatakan terpenuhi.

## Langkah verifikasi berikutnya

1. Jalankan Docker Desktop dalam mode Linux containers. Salin `.env.example` menjadi `.env`, ubah nilai credential lokal, lalu jalankan `docker compose config` dan `docker compose up --build -d`.
2. Periksa status service dan log, verifikasi `/api/products`, `/api/orders`, `/api/reports/heavy`, `/health/live`, serta `/metrics`. Periksa Prometheus Status → Targets dan dashboard Grafana; inspeksi nama/label metrik aktual dan sesuaikan query jika diperlukan.
3. Jalankan `docker compose run --rm k6 run --summary-export=/output/k6-raw.json /scripts/demo.js`; simpan hasil, analisis fase baseline 0–45s, incident 45–150s, recovery 150–210s.
4. Ulangi dengan `DEMO_REPORT_MODE=fast`, recreate API dan jalankan beban sama. Catat metrik aktual, error/dropped iterations, dan keterbatasan tanpa mengarang angka.
