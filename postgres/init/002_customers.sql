-- Tabel customers: contoh tabel tambahan dibuat lewat init script.
-- generate_series() dipakai agar data seed deterministik (id 1-50).
CREATE TABLE IF NOT EXISTS customers (
    id bigint PRIMARY KEY,
    name text NOT NULL,
    email text NOT NULL UNIQUE,
    city text NOT NULL,
    created_at timestamptz NOT NULL
);

INSERT INTO customers (id, name, email, city, created_at)
SELECT id,
       'Customer ' || lpad(id::text, 3, '0'),
       'customer' || id || '@example.com',
       (ARRAY['Jakarta','Bandung','Surabaya','Medan','Denpasar'])[(id % 5) + 1],
       timestamptz '2025-01-01 00:00:00+00' + (id * interval '2 hours')
FROM generate_series(1, 50) AS id
ON CONFLICT (id) DO NOTHING;
