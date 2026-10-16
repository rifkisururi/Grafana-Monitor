VPS IP 100.76.24.117
VPS USER sururi
VPS PASSWORD Coba@123
punya akses sudo, gunakan docker network rifki

# PLAN — Demo Observability ASP.NET Core + PostgreSQL + Prometheus + Grafana

> **Dokumen kerja untuk AI coding agent.** Implementasikan repo runnable end-to-end, bukan hanya skeleton atau dashboard mock. Bahasa README dan dokumentasi: Bahasa Indonesia. Jangan mengarang hasil pengujian; catat metrik hanya setelah benar-benar dijalankan.

## 0. Instruksi eksekusi untuk AI agent

- [ ] Baca seluruh PLAN ini sebelum membuat file.
- [ ] Kerjakan fase 1 sampai 8 secara berurutan; setiap fase wajib bisa diverifikasi.
- [ ] Utamakan demo yang **deterministik, sederhana, dan mudah diulang** di laptop dengan Docker Compose.
- [ ] Jangan berhenti pada setup infrastruktur: buktikan dampak satu endpoint lambat pada dua endpoint lain dengan beban **terukur**, metrik, dan uji kontrol.
- [ ] Jangan mengklaim pengujian berhasil jika container/test benar-benar belum dijalankan. Jika environment tidak mendukung Docker, laporkan keterbatasan dan langkah verifikasi yang tersisa.
- [ ] Simpan instruksi, hasil pengujian faktual, konfigurasi, dan dashboard ke repository agar agent/manusia berikutnya dapat menjalankan demo tanpa setup manual Grafana.
- [ ] Gunakan versi dependency dan Docker image yang **dipin** dan saling kompatibel; catat versi aktual di README. Rekomendasi target: **.NET 10**, **Npgsql 10.x**, PostgreSQL, Prometheus, Grafana OSS, dan k6. Jika paket API berubah, sesuaikan dengan dokumentasi resmi.

## 1. Tujuan demo

### Pertanyaan yang harus bisa dijawab dengan data

1. Endpoint mana yang pertama/utama mengalami latency tinggi?
2. Apakah endpoint `/api/products` dan `/api/orders` ikut lambat **meskipun SQL mereka sederhana**?
3. Apakah kenaikan latency kedua endpoint terjadi karena menunggu koneksi di **Npgsql connection pool yang sama**, bukan karena pekerjaan CPU atau jaringan?
4. Apakah endpoint kontrol `/health/live`, yang **tidak memakai database**, tetap relatif cepat?
5. Ketika beban endpoint berat berhenti, apakah latency endpoint normal kembali pulih?
6. Apakah perubahan konfigurasi/implementasi bisa mengurangi atau menghilangkan efek tersebut?

### Cerita demo satu kalimat

> `GET /api/reports/heavy` menjalankan operasi SQL PostgreSQL yang sengaja lama, menahan koneksi di pool Npgsql terbatas, sehingga request ke `GET /api/products` dan `GET /api/orders` antre saat hendak mengambil koneksi; Prometheus/Grafana memperlihatkan hubungan waktu antara beban heavy, koneksi habis, waktu tunggu koneksi, dan lonjakan p95 latency endpoint normal.

**Catatan ilmiah:** `pg_sleep()` sengaja dipakai sebagai *simulasi operasi database mahal* yang stabil dan dapat diulang; ini **bukan** contoh optimasi SQL produksi. Kesimpulan yang dibuktikan adalah **pool contention / connection starvation**, bukan PostgreSQL CPU saturation atau lock contention.

## 2. Arsitektur

```text
                            k6 (load generator)
                                    |
                                    v
                       ASP.NET Core Web API :8080
                    /api/products  /api/orders  /api/reports/heavy
                          \            |             /
                           \  ONE shared NpgsqlDataSource
                            \ connection pool (max=4)
                                     |
                                     v
                              PostgreSQL :5432
                                     ^
                                     |  (opsional untuk diagnosis)
                              pg_stat_activity

ASP.NET Core /metrics ----scrape (5s)---> Prometheus :9090
         |                                   |
         +-- HTTP duration/rate/errors       +--> Grafana :3000
         +-- Npgsql pool state/limits                 (provisioned)
         +-- custom pool wait/lease metrics
```

**Semua container** ada di jaringan Docker Compose yang sama. Prometheus scrape `http://api:8080/metrics`; Grafana memakai datasource `http://prometheus:9090` (bukan `localhost` antar-container). k6 memanggil `http://api:8080`.

### Komponen wajib

| Komponen | Fungsi | Catatan |
|---|---|---|
| ASP.NET Core minimal API | Empat endpoint + instrumentasi HTTP dan Npgsql | Satu instance API; **hindari** scaling beberapa replica karena mempersulit analisis pool |
| PostgreSQL | Menyimpan tabel products/orders serta menjalankan query lambat | Seed database idempoten/terdokumentasi |
| Npgsql | Satu sumber koneksi bersama | `Maximum Pool Size=4`, pool diaktifkan |
| OpenTelemetry .NET Metrics | Instrumentasi request, Npgsql, dan metrik aplikasi | Ekspos `/metrics` untuk scrape Prometheus |
| Prometheus | Scrape metrik tiap 5 detik dan simpan time series | Health/targets terverifikasi UP |
| Grafana OSS | Dashboard otomatis tersedia | Datasource/dashboard provisioning berbasis file |
| k6 | Menciptakan baseline → gangguan → recovery | Traffic produk dan order harus konsisten di seluruh fase |

**Tidak perlu** Kubernetes, message broker, Jaeger/Tempo, Loki, OpenTelemetry Collector, PostgreSQL exporter, frontend React, atau authentication untuk MVP. Boleh ditambahkan *setelah* bukti utama selesai, jika benar-benar ada nilai tambah.

## 3. Desain API & database

### 3.1 Endpoint

| Endpoint | Query/operasi | Ekspektasi |
|---|---|---|
| `GET /api/products` | `SELECT id, name, price FROM products ORDER BY id LIMIT 20` | Cepat jika pool tidak sibuk |
| `GET /api/orders` | `SELECT id, product_id, quantity, created_at FROM orders ORDER BY id DESC LIMIT 20` | Cepat jika pool tidak sibuk |
| `GET /api/reports/heavy` | Buka koneksi dari **pool yang sama**, eksekusi `SELECT pg_sleep(3)` lalu `SELECT COUNT(*) FROM orders` **sebelum melepaskan koneksi** | Perlu ~3 detik atau lebih, menahan satu lease koneksi |
| `GET /health/live` | Return `{ "status": "ok" }`, **tanpa koneksi database** | Kontrol negatif; seharusnya tidak terpengaruh langsung oleh pool |
| `GET /metrics` | OpenTelemetry Prometheus exporter | Dipakai Prometheus, tidak dihitung sebagai API bisnis |

**PENTING:** Endpoint lambat harus *benar-benar mempertahankan koneksi PostgreSQL selama `pg_sleep`*; jangan memakai `Thread.Sleep`, `Task.Delay` di luar koneksi, membuat pool baru per endpoint, atau menggunakan koneksi berbeda untuk products/orders/heavy. Jika `pg_sleep` hanya ditempatkan sebelum `OpenConnectionAsync`, demonstrasi dianggap gagal.

Implementasikan Npgsql async (`OpenConnectionAsync`, `ExecuteReaderAsync`, `ExecuteScalarAsync`, dll.) dengan `CancellationToken` request. Pakai `await using`/`try-finally` untuk memastikan koneksi selalu dikembalikan ke pool saat sukses, error, ataupun cancellation. Jangan gunakan transaksi terbuka berkepanjangan yang tidak diperlukan.

### 3.2 Data dan SQL

- Buat `products(id bigint primary key, name text not null, price numeric(12,2) not null)`.
- Buat `orders(id bigint primary key, product_id bigint not null references products(id), quantity int not null, created_at timestamptz not null)`.
- Isi minimal 100 products dan 1.000 orders secara deterministik lewat `postgres/init/001_schema_seed.sql` atau migrasi/seed yang bisa diulang. Pastikan query cepat dan indeks memadai (`orders.id` sudah diindeks oleh PK).
- Gunakan data dummy, tanpa PII, identitas nyata, atau database eksternal.
- Tambahkan query diagnosis untuk DBA (lihat bagian 7).

### 3.3 Konfigurasi fault injection

Default environment development demo:

```dotenv
ASPNETCORE_ENVIRONMENT=Development
ConnectionStrings__AppDb=Host=postgres;Port=5432;Database=monitor_demo;Username=demo;Password=<from-env>;Pooling=true;Maximum Pool Size=4;Timeout=15;Command Timeout=20;Application Name=monitor-demo-api
DEMO_HEAVY_SLEEP_SECONDS=3
DEMO_REPORT_MODE=slow
```

- Password **jangan di-hardcode di repo**; contoh `.env.example` berisi placeholder, Docker Compose membaca `.env` (boleh menawarkan nilai dummy *hanya untuk lokal*, tidak untuk produksi).
- Validasi `DEMO_HEAVY_SLEEP_SECONDS` dengan batas aman, misalnya 0–5 detik, dan jangan menyusun input user ke SQL mentah. Parameterisasikan nilai sleep atau gunakan ekspresi SQL dengan parameter yang kompatibel.
- `DEMO_REPORT_MODE=slow` = simulate expensive SQL; `fast` = jalur laporan tanpa `pg_sleep`, untuk eksperimen perbaikan. Perubahan env membutuhkan restart API; dokumentasikan dan **jangan menafsirkan restart sebagai pemulihan alami**.
- Mode `fast` digunakan sebagai *model pengurangan DB work*, bukan bukti query optimizer telah memperbaiki SQL nyata.
- Untuk opsi mitigasi terpisah, jalankan ulang dengan `Maximum Pool Size=20`, lalu jelaskan ini mengurangi antrean tetapi **tidak memperbaiki query lambat** dan bukan solusi universal di produksi.

### 3.4 Desain akses data

- Daftarkan **satu** `NpgsqlDataSource` singleton dengan `NpgsqlDataSourceBuilder`, idealnya `Name = "DemoDbPool"` agar label metrik jelas dan tidak mengekspos password melalui pool name.
- Semua endpoint database menggunakan singleton tersebut. Jangan `new NpgsqlDataSourceBuilder(...).Build()` per request dan jangan ganti connection string per endpoint.
- Bungkus pengambilan koneksi di helper/abstraksi kecil yang sama (`IConnectionLease`/`DbConnectionExecutor`) agar instrumentasi waktu tunggu konsisten.
- Rekam secara terpisah: (1) waktu menunggu/membuka koneksi, (2) durasi perintah SQL, (3) total durasi HTTP. Tanpa pemisahan ini, penyebab ikut-lambat akan sulit dibuktikan.
- Jangan mengekspos password, raw connection string, query parameter, atau ID per-request sebagai label metric (risiko sensitive data dan high cardinality).

## 4. Instrumentasi yang wajib tersedia

### 4.1 Metrik bawaan

Gunakan OpenTelemetry SDK dan register meters/sources yang relevan; untuk .NET 10 setidaknya `Microsoft.AspNetCore.Hosting` dan `Npgsql`.

1. `http.server.request.duration` histogram (umumnya muncul di Prometheus sebagai `http_server_request_duration_seconds_*`). Pastikan label `http_route` tersedia.
2. `http.server.active_requests` / throughput/error HTTP (gunakan histogram `_count` untuk hitungan jika diperlukan).
3. Npgsql pool: `db.client.connection.count` dengan `state=used|idle` dan `db.client.connection.max`; Npgsql 10 memakai penamaan yang mengikuti semantic conventions terkini.
4. Opsional: `db.client.operation.duration` untuk mengamati durasi eksekusi SQL; verifikasi metrik dan label nyata di `/metrics`.

**Jangan mengasumsikan semua metrik semantic conventions ada secara otomatis.** Khususnya `db.client.connection.pending_requests` tidak boleh dijadikan satu-satunya sumber untuk waiter: ketersediaannya tergantung implementasi dan versi Npgsql. Gunakan metrik custom di bawah sebagai sumber pembuktian primer.

### 4.2 Metrik custom wajib

Gunakan `System.Diagnostics.Metrics.Meter` milik aplikasi, dengan nama stabil, misalnya `Demo.Api`:

| Nama instrument | Tipe | Pengukuran |
|---|---|---|
| `demo_db_pool_acquire_seconds` | Histogram<double> | Stopwatch sejak **sebelum** `OpenConnectionAsync` hingga sukses/gagal (detik); rekam untuk route products/orders/heavy, dengan `endpoint` label dari set nilai tetap |
| `demo_db_pool_waiters` | ObservableGauge<long> | Jumlah operasi yang sedang menunggu `OpenConnectionAsync`, didapat dari `Interlocked` (increment saat mulai menunggu, decrement dalam finally) |
| `demo_db_connection_leased` | ObservableGauge<long> | Jumlah koneksi yang telah diperoleh dan belum dikembalikan, menggunakan `Interlocked` dan lifecycle yang benar |
| `demo_db_query_seconds` | Histogram<double> | Waktu SQL saja, terpisah dari acquire time; `endpoint` label rendah kardinalitas |

**Wajib:** tidak ada nilai waiter atau leased negatif; decrement tetap terjadi bila exception atau cancellation; tidak double-decrement; gauge merefleksikan *lease oleh aplikasi*, bukan jumlah socket PostgreSQL total. Gunakan label `endpoint=products|orders|heavy` untuk histogram; untuk gauges global cukup label pool name konstan bila perlu. Jangan label gauge dengan requestId.

Kalau nama yang diekspos Prometheus berbeda akibat konversi SDK/exporter (termasuk suffix `seconds`), **inspeksi `/metrics` dan perbaiki PromQL berdasarkan output aktual**. Dokumentasikan nama nyata pada README. Jangan membuat dashboard yang kosong karena menebak nama label/metrik.

### 4.3 Ekspor Prometheus

- Ekspos `GET /metrics` dengan `OpenTelemetry.Exporter.Prometheus.AspNetCore` + `MapPrometheusScrapingEndpoint()` sesuai API paket terpasang. Paket exporter ini dapat berstatus prerelease: pin versi teruji dan beri catatan di README.
- Custom histogram gunakan bucket yang bisa membedakan sub-100ms vs beberapa detik. Contoh batas detik: `0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2, 3, 5, 10, 20`.
- Tambahkan `service.name=log-monitor-demo-api` sebagai resource attribute, tanpa label high cardinality.
- Proteksi `/metrics` dari akses internet/public pada deployment non-demo (untuk Docker lokal cukup internal network/port yang terkontrol).

## 5. Docker Compose & provisioning

Buat `docker-compose.yml` atau `compose.yaml` dengan service:

1. `postgres`: PostgreSQL, healthcheck (`pg_isready`), named volume data, mount SQL init scripts, credential dari `.env`.
2. `api`: multi-stage Dockerfile untuk .NET 10 (SDK build, runtime aspnet), listen `0.0.0.0:8080`, host port `8080`, environment connection string, depends_on PostgreSQL sehat, healthcheck bila dapat menggunakan executable yang tersedia.
3. `prometheus`: config scrape interval `5s`, target `api:8080`, job `aspnet-api`, port host `9090`, named volume data, verifikasi target UP.
4. `grafana`: host `3000`, datasource Prometheus *provisioned*, dashboard JSON *provisioned*, volume Grafana, admin credential dari `.env`.
5. `k6`: image resmi Grafana k6, mount `./loadtest` ke `/scripts` dan `./artifacts` ke `/output`, jalankan via `docker compose run --rm k6 run /scripts/demo.js`; gunakan `profiles: ["tools"]` agar tidak jalan setiap `up`.

Buat `prometheus/prometheus.yml`, `grafana/provisioning/datasources/prometheus.yml`, `grafana/provisioning/dashboards/dashboards.yml`, dan `grafana/dashboards/api-pool-contention.json`.

- Pin image tags eksplisit (hindari `latest`). Pilih image/tag yang benar-benar tersedia saat implementasi.
- Pastikan `docker compose up --build -d` sampai healthy, init schema/seed berhasil, `/metrics` menyajikan teks Prometheus, `/api/products` mengembalikan rows.
- README mencantumkan port dan cara mengatasi port bentrok.
- `docker compose down -v` **menghapus data dan metrik**; jangan gunakan jika evidence belum disimpan.

## 6. Dashboard Grafana wajib

Dashboard berjudul **"API Incident Lab — PostgreSQL Pool Contention"** yang langsung tersedia setelah startup. Gunakan satuan yang benar (ms, req/s, count, %), legend endpoint yang mudah dibaca, refresh 5s, time range 15m default. Usahakan menaruh panel berikut dalam satu viewport/2 section.

| Panel | Visualisasi | Tujuan |
|---|---|---|
| **HTTP p95 latency by route** | Time series, detik dikonversi jadi ms | Lihat heavy serta kenaikan products/orders pada waktu yang sama |
| **HTTP request rate by route** | Time series req/s | Pastikan beban fast endpoints tetap, bukan efek workload yang berubah |
| **HTTP 5xx/error rate** | Time series req/s atau % | Lihat timeout jika pool antre terlalu lama |
| **Npgsql connections used vs max** | Time series/gauge | Lihat kapasitas pool mencapai limit 4 |
| **DB pool waiters** | Time series/gauge | Antrean `OpenConnectionAsync` saat incident |
| **DB pool acquisition p95 by endpoint** | Time series ms | Buktikan products/orders lebih banyak **menunggu koneksi** |
| **SQL execution p95 by endpoint** | Time series ms | Buktikan SQL products/orders tetap singkat setelah koneksi didapat |
| **Non-DB health p95** | Time series ms | Kontrol negatif agar tidak salah menyimpulkan host total overload |
| **Active HTTP requests** | Time series | Korelasi backlog di level API |

Contoh PromQL (sesuaikan dengan **nama dan label yang nyata** sesudah inspeksi `/metrics`):

```promql
# Request latency p95 per route, satuan detik; panel Grafana ubah ke ms
histogram_quantile(0.95,
  sum by (le, http_route) (
    rate(http_server_request_duration_seconds_bucket{job="aspnet-api"}[30s])
  )
)

# RPS per route
sum by (http_route) (
  rate(http_server_request_duration_seconds_count{job="aspnet-api"}[30s])
)

# Pool wait p95 per endpoint, satuan detik
histogram_quantile(0.95,
  sum by (le, endpoint) (
    rate(demo_db_pool_acquire_seconds_bucket{job="aspnet-api"}[30s])
  )
)

# Jumlah waiter custom
sum(demo_db_pool_waiters{job="aspnet-api"})

# Jumlah koneksi Npgsql used; periksa nama metric aktual terlebih dahulu
sum(db_client_connection_count{job="aspnet-api",state="used"})

# Konfigurasi maksimum koneksi; periksa nama metric aktual terlebih dahulu
sum(db_client_connection_max{job="aspnet-api"})

# Durasi query saja (p95) by endpoint, detik
histogram_quantile(0.95,
  sum by (le, endpoint) (
    rate(demo_db_query_seconds_bucket{job="aspnet-api"}[30s])
  )
)
```

- **PENTING:** query histogram `histogram_quantile` membutuhkan metrik `_bucket` dan label `le`; bila SDK menyajikan native histogram atau nama lain, pakai expression yang kompatibel. Verifikasi panel bukan `No data`.
- Gunakan interval `[30s]`/`[1m]` yang cukup panjang untuk data scrape 5s; legend dan filter route eksplisit supaya `/metrics` tidak mendominasi.
- Bila tersedia, tandai fase **baseline (0–45s)**, **incident (45–150s)**, **recovery (150–210s)** lewat anotasi atau panduan range di README. Jangan menampilkan timestamp palsu.

## 7. k6 load test — pembuktian utama

### 7.1 Satu pengujian 3 fase

Buat `loadtest/demo.js` dengan **empat scenario paralel** dan `constant-arrival-rate` (open model):

| Scenario | Start | Durasi | Target | Target rate awal |
|---|---:|---:|---|---:|
| `fast_products` | `0s` | `210s` | `/api/products` | 2 iterasi/s |
| `fast_orders` | `0s` | `210s` | `/api/orders` | 2 iterasi/s |
| `slow_report` | `45s` | `105s` | `/api/reports/heavy` | 2 iterasi/s |
| `health_control` | `0s` | `210s` | `/health/live` | 1 iterasi/s |

Gunakan empat scenario sesuai tabel, masing-masing dengan fungsi JS berbeda. Gunakan `startTime` dan `duration` sesuai tabel; jangan memulai heavy saat baseline. Format `BASE_URL` default `http://api:8080` saat k6 dalam Docker.

**Rasional konfigurasi:** Query heavy memegang koneksi ~3 detik; 2 request/detik menginginkan rata-rata ~6 koneksi sibuk, sementara `Maximum Pool Size=4`. Dengan beban yang bertahan, pool berpotensi jenuh dan fast endpoints ikut mengantre. Ini adalah *prediksi eksperimen*, bukan hasil terukur; angka aktual harus diuji.

- Set `preAllocatedVUs` dan `maxVUs` yang cukup agar `dropped_iterations` kecil; start misalnya 50 VU untuk masing-masing fast, 50 untuk heavy, 20 untuk health, lalu kalibrasi sesuai resources. Boleh lebih hemat bila terbukti tanpa dropped iterations.
- Set tag eksplisit `endpoint=products|orders|heavy|health` pada masing-masing request atau scenario agar hasil k6 dapat dianalisis per endpoint.
- Rekam `http_req_duration`, `http_req_failed`, `dropped_iterations`, iterations/s dan status responses; jangan hanya melihat rata-rata global.
- Hindari `sleep()` manual dalam scenario `constant-arrival-rate` karena kedatangan sudah diatur executor.
- Kegagalan/timeout di fase incident **boleh terjadi dan harus dilaporkan**; jangan membuat threshold global yang menggagalkan skrip sebelum fase recovery. Pisahkan SLO baseline/incident pada analisis setelah run.
- Simpan output summary k6 ke `artifacts/` dengan cara yang didukung versi k6 terpasang (`handleSummary` atau flag yang telah diverifikasi). Sertakan exit status.

### 7.2 Interpretasi fase

1. **Baseline 0–45s:** hanya products, orders, health. Latency products/orders rendah, acquisition p95 kecil, pool waiters mendekati nol.
2. **Incident 45–150s:** heavy mulai datang. Periksa pool used mendekati/max 4, waiters naik, acquisition p95 products/orders naik, HTTP p95 products/orders ikut naik, SQL products/orders sendiri tetap cepat.
3. **Recovery 150–210s:** heavy berhenti, request pending diberi waktu drain. Products/orders diharapkan kembali ke baseline; gali dampak timeout jika antrean masih menumpuk. `/health/live` diharapkan relatif stabil di semua fase.

### 7.3 Kriteria bukti (acceptance criteria)

Semua berikut harus terpenuhi untuk menyatakan demo **berhasil membuktikan spillover**:

- [ ] Waktu kedatangan `slow_report` sesuai jadwal dan fast endpoint rates relatif konstan sebelum/selama incident. `dropped_iterations` rendah, atau analisis mengakui keterbatasannya.
- [ ] p95 HTTP products **dan** orders pada fase incident naik **minimal 3x** dibanding baseline **dan** naik setidaknya **300 ms** secara absolut. Jika hardware menimbulkan variasi, tampilkan hasil aktual dan ulangi hingga sinyal jelas, bukan mengganti angka.
- [ ] During incident, koneksi `used` mencapai batas pool **4** atau sesuai konfigurasi dan custom `demo_db_pool_waiters` positif pada beberapa titik pengamatan.
- [ ] Durasi acquire koneksi products/orders meningkat searah dengan latency HTTP; durasi SQL *setelah koneksi diperoleh* products/orders relatif rendah. Ini bukti mekanisme, bukan korelasi HTTP saja.
- [ ] `/health/live` tidak melonjak sebanding dengan endpoint DB; jika ikut lambat, telusuri bottleneck global/infrastruktur dan ulangi setelah diperbaiki.
- [ ] Recovery terbukti setelah request heavy berhenti: p95 normal turun mendekati baseline (boleh ada masa drain; nyatakan jika gagal).
- [ ] Prometheus target UP, seluruh panel kritikal Grafana memiliki data, tidak error atau kosong.
- [ ] Simpan raw result dan ringkasan berbasis angka dari run aktual.

**Jika kondisi belum tercapai**, agent wajib diagnosis dan kalibrasi: cek pool benar-benar bersama, cek `pg_sleep` dieksekusi *sesudah* koneksi diperoleh, cek metrik pool, lalu (sesuai batas aman lokal) uji `Maximum Pool Size=3`, `DEMO_HEAVY_SLEEP_SECONDS=5`, dan/atau tingkatkan rate heavy secara terbatas. Jangan memalsukan screenshot atau memakai delay aplikasi untuk menciptakan efek tanpa shared pool.

### 7.4 Kontrol tambahan dan eksperimen perbaikan

Lakukan **setidaknya satu** eksperimen setelah run utama:

- **Eksperimen A — kurangi durasi kerja DB (direkomendasikan):** `DEMO_REPORT_MODE=fast`, restart API, jalankan beban yang sama, bandingkan p95 products/orders + waktu tunggu koneksi. Jelaskan `fast` merupakan simulasi perbaikan kerja SQL, bukan optimasi query nyata.
- **Eksperimen B — pool diperbesar (opsional):** `Maximum Pool Size=20`, tetap `DEMO_REPORT_MODE=slow`, jalankan beban sama. Heavy masih lambat ~3s, tetapi spillover ke fast endpoints seharusnya jauh berkurang. Jelaskan konsekuensi ekstra koneksi pada PostgreSQL dan mengapa ini bukan solusi produksi otomatis.

**Kontrol kausal utama:** heavy workload adalah satu-satunya workload yang berubah pada batas fase incident, sedangkan fast endpoint rates tetap; hasil pengujian mitigasi menjadi penguat diagnosis.

### 7.5 Diagnosis PostgreSQL (tanpa exporter tambahan)

Simpan contoh perintah psql yang dapat dijalankan selama fase incident:

```sql
SELECT pid, application_name, state, wait_event_type, wait_event,
       now() - query_start AS running_for, LEFT(query, 120) AS query_preview
FROM pg_stat_activity
WHERE application_name = 'monitor-demo-api'
ORDER BY query_start NULLS LAST;
```

Harapan: beberapa sesi Npgsql sedang menjalankan `SELECT pg_sleep(...)`; angka ini **tidak** menunjukkan request yang antre di sisi Npgsql karena permintaan yang belum mendapat connection belum muncul di `pg_stat_activity`. Karena itulah **custom pool waiters/acquisition histogram** wajib dipakai.

## 8. Deliverables repository

Struktur yang diinginkan (boleh penyesuaian minor asal konsisten):

```text
.
├── plan.md                                  # dokumen ini
├── README.md                                # quickstart & demo walkthrough
├── .env.example
├── .gitignore
├── compose.yaml
├── src/
│   └── Demo.Api/
│       ├── Demo.Api.csproj
│       ├── Dockerfile
│       ├── Program.cs
│       ├── Data/                           # SQL/data access + singleton source
│       ├── Metrics/                        # custom instrumentation
│       └── appsettings.json
├── postgres/
│   └── init/001_schema_seed.sql
├── prometheus/prometheus.yml
├── grafana/
│   ├── provisioning/
│   │   ├── datasources/prometheus.yml
│   │   └── dashboards/dashboards.yml
│   └── dashboards/api-pool-contention.json
├── loadtest/demo.js
├── tests/                                   # integration/smoke checks
└── artifacts/
    ├── .gitkeep
    ├── evidence.md                          # hanya hasil pengujian aktual
    └── ...                                  # hasil k6/grafik saat tersedia
```

README wajib mengandung:

1. Prasyarat: Docker Engine/Desktop + Compose; CPU/RAM/disk estimasi wajar jika sudah diuji.
2. Cara `cp .env.example .env`, isi nilai development, `docker compose up --build -d`.
3. URL: API `http://localhost:8080`, Grafana `http://localhost:3000`, Prometheus `http://localhost:9090`.
4. Cara verifikasi HTTP endpoint, `/metrics`, Prometheus `Status > Targets`, datasource dan dashboard Grafana.
5. Perintah menjalankan k6: `docker compose run --rm k6 run /scripts/demo.js` (pastikan compose mount dan profile mendukung).
6. Skrip presentasi demo (lihat bagian 10) dan cara membaca 3 fase pada dashboard.
7. Cara menjalankan eksperimen mitigasi dan mengembalikan pengaturan default.
8. Troubleshooting (koneksi PG, seed lama, Npgsql metrics tidak muncul, `/metrics` kosong, PromQL salah, port bentrok, k6 dropped iterations, `pg_sleep` dibatalkan karena timeout).
9. Batasan demo dan peringatan keamanan; jangan mengekspos layanan ini publik.
10. Cara teardown tanpa dan dengan penghapusan data (`docker compose down` vs `docker compose down -v`).

## 9. Fase implementasi & definition of done

### Fase 1 — Project bootstrap

- [ ] Inisialisasi solution/proyek .NET 10 minimal API, Dockerfile multi-stage, compose.
- [ ] Seluruh dependency terpin dan build dapat sukses.
- [ ] `docker compose config` valid; tidak ada credential hardcoded yang ter-commit.

**DoD:** `docker compose up --build -d` berhasil membangun dan menjalankan API + DB.

### Fase 2 — PostgreSQL dan endpoints

- [ ] Tabel/seed, singleton `NpgsqlDataSource`, empat endpoint.
- [ ] All 3 DB endpoints memakai pool yang sama; koneksi dilepas pada semua code path.
- [ ] Unit/smoke test endpoint normal cepat dan heavy menjalankan `pg_sleep` dari koneksi aktif.

**DoD:** Products/orders memberi data real; heavy ~3 detik single request; health non-DB cepat.

### Fase 3 — Metrik dan Prometheus

- [ ] Instrumentasi HTTP, Npgsql, custom acquire/waiters/leased/query.
- [ ] `/metrics` berisi metrik terkait setelah endpoint dipanggil.
- [ ] Prometheus scrape `api:8080` UP dan time series terlihat.

**DoD:** Nama metrik, labels, bucket dan unit diinspeksi dari output aktual.

### Fase 4 — Dashboard Grafana

- [ ] Datasource + dashboard auto-provision.
- [ ] Semua panel penting memiliki PromQL valid & data setelah traffic.
- [ ] Menampilkan per-route, pool, SQL, acquisition, dan control health.

**DoD:** Setelah `docker compose up` baru, user tidak perlu import JSON/manual edit data source.

### Fase 5 — k6 demo.js

- [ ] Traffic steady normal+health, heavy hanya dari detik ke-45 sampai ke-150.
- [ ] Output k6 tersimpan dengan metrik terpisah per endpoint.
- [ ] Rate dan VUs dikalibrasi sehingga dropped_iterations tidak merusak kesimpulan.

**DoD:** 210 detik run selesai atau batasan konkret didokumentasikan.

### Fase 6 — Pengumpulan bukti dan analisis

- [ ] Run baseline→incident→recovery secara end-to-end.
- [ ] Catat p95 HTTP per endpoint pada setiap fase, p95 acquire, p95 SQL, peak waiter, used/max connections, error rates, dropped iterations.
- [ ] Perlihatkan kontrol `/health/live` dan PostgreSQL `pg_stat_activity` saat incident jika bisa.
- [ ] Tuliskan kesimpulan **berdasarkan angka terukur** di `artifacts/evidence.md`, sertai grafik/screenshots asli jika memungkinkan.

**DoD:** Pembaca dapat mengikuti hubungan: **heavy load → pool saturasi → waktu acquire naik → dua API normal melambat → pulih setelah heavy berhenti**.

### Fase 7 — Eksperimen mitigasi

- [ ] Jalankan ulang dengan `DEMO_REPORT_MODE=fast` (atau pool lebih besar, dipisah sebagai mitigasi) pada load identik.
- [ ] Bandingkan p95 before/after dan catat trade-off/limitations.

**DoD:** Ada bukti empiris perbaikan, bukan hanya saran teoretis.

### Fase 8 — Dokumentasi dan serah terima

- [ ] README lengkap Bahasa Indonesia, command copy/paste, arsitektur, runbook demo.
- [ ] Smoke test/automated validation untuk API & observability sebanyak yang masuk akal.
- [ ] Pastikan repo tidak menyimpan data sensitif dan file output besar yang tidak perlu.
- [ ] Report akhir mencakup pekerjaan selesai, bukti, cara menjalankan, dan pekerjaan yang tertinggal.

**DoD:** Orang lain dapat clone, run compose, run k6, dan memahami dashboard tanpa informasi di luar repository.

## 10. Script demo untuk presentasi (5–8 menit)

1. **Buka dashboard Grafana:** tunjukkan panel p95 latency route, DB pool used/max, pool waiters, DB acquisition p95, dan health endpoint.
2. **Jelaskan baseline:** buka `GET /api/products` dan `/api/orders`; kedua query DB singkat, p95 dan wait mendekati nol.
3. **Jalankan k6:** `docker compose run --rm k6 run /scripts/demo.js`. Saat 0–45 detik, hanya fast traffic; tunjukkan dashboard bergerak.
4. **Pada detik 45:** k6 mulai memanggil `/api/reports/heavy` 2/s. Tunjukkan endpoint heavy lama, Npgsql connection pool max, pool waiter bertambah, p95 products/orders turut naik meskipun request rate keduanya tidak dinaikkan.
5. **Tunjukkan kausalitas:** p95 acquisition products/orders naik, p95 SQL products/orders sendiri tetap rendah; health non-DB tetap relatif stabil. Jika perlu, tunjukkan sesi `pg_sleep` lewat `pg_stat_activity`.
6. **Pada detik 150:** heavy berhenti; perlihatkan pemulihan dan antrean turun selama fase recovery. Jelaskan mengapa p95 perlu waktu untuk pulih akibat window PromQL.
7. **Perbaikan:** jalankan ulang mode `fast` (atau pool lebih besar), bandingkan grafik/evidence; tekankan perbedaan **root-cause improvement** versus **capacity mitigation**.
8. **Kesimpulan:** “Monitoring memperlihatkan bahwa dua endpoint normal lambat **bukan karena query-nya sendiri**, melainkan karena koneksi database bersama habis dipakai endpoint berat.”

## 11. Definisi sukses keseluruhan

Demo selesai jika:

- API + PostgreSQL + Prometheus + Grafana + k6 runnable memakai Docker Compose;
- Produk/order memakai query tabel PostgreSQL nyata, heavy memegang koneksi karena simulasi operasi SQL lama;
- Dashboard memperlihatkan HTTP latency **dan** bukti pool saturation/wait;
- Load test 3 fase menunjukkan dampak pada **minimal dua endpoint lain**;
- Negative control non-DB diperiksa;
- Hasil aktual dikumpulkan secara transparan; dan
- README menyediakan instruksi sekali jalan untuk mengulang presentasi.

**Non-goals:** benchmark produksi, kapasitas database nyata, load test internet, distributed tracing penuh, microservice architecture, dan optimasi SQL sungguhan. Konfigurasi kecil/sengaja bermasalah digunakan **hanya untuk demonstrasi di localhost**.

## 12. Referensi teknis yang perlu dicek agent saat coding

- ASP.NET Core .NET 10 HTTP metrics: https://learn.microsoft.com/en-us/aspnet/core/metrics/http?view=aspnetcore-10.0
- ASP.NET Core OpenTelemetry + Prometheus scraping: https://learn.microsoft.com/en-us/aspnet/core/metrics/overview?view=aspnetcore-10.0
- Npgsql OpenTelemetry metrics (termasuk perubahan nama di Npgsql 10): https://www.npgsql.org/doc/diagnostics/metrics.html
- Npgsql connection string/pool size/timeout: https://www.npgsql.org/doc/connection-string-parameters.html
- Grafana datasource & dashboard provisioning: https://grafana.com/docs/grafana/latest/administration/provisioning/
- k6 scenarios: https://grafana.com/docs/k6/latest/using-k6/scenarios/
- k6 constant-arrival-rate: https://grafana.com/docs/k6/latest/using-k6/scenarios/executors/constant-arrival-rate/
- k6 dropped iterations and sizing VUs: https://grafana.com/docs/k6/latest/using-k6/scenarios/concepts/arrival-rate-vu-allocation/

---

**Prioritas agent:** Bukti eksperimen > dekorasi dashboard > fitur tambahan. Jangan mengubah mekanisme kerusakan utama atau menghapus kontrol negatif demi membuat demo “terlihat bagus”.
