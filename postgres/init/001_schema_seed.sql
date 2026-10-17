CREATE TABLE IF NOT EXISTS products (
    id bigint PRIMARY KEY,
    name text NOT NULL,
    price numeric(12,2) NOT NULL
);

CREATE TABLE IF NOT EXISTS orders (
    id bigint PRIMARY KEY,
    product_id bigint NOT NULL REFERENCES products(id),
    quantity int NOT NULL,
    created_at timestamptz NOT NULL
);

INSERT INTO products (id, name, price)
SELECT id, 'Product ' || lpad(id::text, 3, '0'), (id * 1.25)::numeric(12,2)
FROM generate_series(1, 100) AS id
ON CONFLICT (id) DO NOTHING;

INSERT INTO orders (id, product_id, quantity, created_at)
SELECT id, ((id - 1) % 100) + 1, ((id - 1) % 5) + 1,
       timestamptz '2025-01-01 00:00:00+00' + (id * interval '1 minute')
FROM generate_series(1, 1000) AS id
ON CONFLICT (id) DO NOTHING;
