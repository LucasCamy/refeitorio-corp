
CREATE TABLE companies (
    id uuid PRIMARY KEY, name text NOT NULL, timezone text NOT NULL
);
CREATE TABLE units (
    id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES companies(id), name text NOT NULL,
    UNIQUE (company_id, id)
);
CREATE TABLE terminals (
    id uuid PRIMARY KEY, unit_id uuid NOT NULL REFERENCES units(id), name text NOT NULL,
    UNIQUE (unit_id, id)
);
CREATE TABLE staff_users (
    id uuid PRIMARY KEY, username text UNIQUE NOT NULL, display_name text NOT NULL,
    password_hash text NOT NULL, role text NOT NULL CHECK (role IN ('operator', 'manager', 'admin')),
    terminal_id uuid NOT NULL REFERENCES terminals(id), active boolean NOT NULL DEFAULT true
);
CREATE TABLE employees (
    id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES companies(id), rf text NOT NULL,
    name text NOT NULL, department text NOT NULL, employment text NOT NULL,
    external_id text NOT NULL, active boolean NOT NULL, eligible boolean NOT NULL, payroll_allowed boolean NOT NULL,
    UNIQUE (company_id, id), UNIQUE (company_id, rf), UNIQUE (company_id, external_id)
);
CREATE TABLE badges (
    company_id uuid NOT NULL, employee_id uuid NOT NULL, token_hash bytea NOT NULL,
    active boolean NOT NULL DEFAULT true, PRIMARY KEY (company_id, token_hash),
    FOREIGN KEY (company_id, employee_id) REFERENCES employees(company_id, id)
);
CREATE TABLE products (
    id uuid PRIMARY KEY, company_id uuid NOT NULL REFERENCES companies(id), name text NOT NULL,
    description text NOT NULL, category text NOT NULL CHECK (category IN ('beverage', 'dessert', 'meal', 'other')),
    price_cents integer NOT NULL CHECK (price_cents BETWEEN 0 AND 1000000),
    subsidy_cents integer NOT NULL CHECK (subsidy_cents BETWEEN 0 AND price_cents),
    barcode text, image_key text CHECK (image_key IN ('juice','cola','flan','brownie','takeout')),
    available boolean NOT NULL, version integer NOT NULL DEFAULT 1,
    UNIQUE (company_id, id), UNIQUE (company_id, barcode)
);
CREATE TABLE meal_rules (
    company_id uuid PRIMARY KEY REFERENCES companies(id), name text NOT NULL, description text NOT NULL,
    starts_at time NOT NULL, ends_at time NOT NULL CHECK (ends_at > starts_at),
    price_cents integer NOT NULL CHECK (price_cents BETWEEN 0 AND 1000000),
    subsidy_cents integer NOT NULL CHECK (subsidy_cents BETWEEN 0 AND price_cents),
    meal_limit integer NOT NULL CHECK (meal_limit BETWEEN 1 AND 10),
    daily_limit_cents bigint CHECK (daily_limit_cents >= 0),
    monthly_limit_cents bigint CHECK (monthly_limit_cents >= 0),
    version integer NOT NULL DEFAULT 1
);
CREATE TABLE checkout_sessions (
    employee_id uuid PRIMARY KEY, company_id uuid NOT NULL, terminal_id uuid NOT NULL REFERENCES terminals(id),
    operator_id uuid NOT NULL REFERENCES staff_users(id), token uuid UNIQUE NOT NULL, expires_at timestamptz NOT NULL,
    FOREIGN KEY (company_id, employee_id) REFERENCES employees(company_id, id)
);
CREATE TABLE orders (
    id uuid PRIMARY KEY, company_id uuid NOT NULL, unit_id uuid NOT NULL,
    terminal_id uuid NOT NULL, employee_id uuid NOT NULL, operator_id uuid NOT NULL REFERENCES staff_users(id),
    business_date date NOT NULL, created_at timestamptz NOT NULL,
    status text NOT NULL CHECK (status IN ('confirmed','reversed')),
    includes_meal boolean NOT NULL, subtotal_cents bigint NOT NULL CHECK (subtotal_cents >= 0),
    subsidy_cents bigint NOT NULL CHECK (subsidy_cents BETWEEN 0 AND subtotal_cents),
    total_cents bigint NOT NULL CHECK (total_cents = subtotal_cents - subsidy_cents),
    idempotency_key uuid NOT NULL, request_hash text NOT NULL,
    checkout_token uuid UNIQUE NOT NULL,
    UNIQUE (operator_id, idempotency_key),
    FOREIGN KEY (company_id, unit_id) REFERENCES units(company_id, id),
    FOREIGN KEY (unit_id, terminal_id) REFERENCES terminals(unit_id, id),
    FOREIGN KEY (company_id, employee_id) REFERENCES employees(company_id, id)
);
CREATE INDEX orders_employee_active_date ON orders (company_id, employee_id, business_date) WHERE status = 'confirmed';
CREATE INDEX orders_unit_date ON orders (company_id, unit_id, business_date, created_at DESC);
CREATE TABLE order_items (
    order_id uuid NOT NULL REFERENCES orders(id), position integer NOT NULL,
    product_id uuid, name text NOT NULL, quantity integer NOT NULL CHECK (quantity BETWEEN 1 AND 20),
    unit_price_cents integer NOT NULL CHECK (unit_price_cents >= 0),
    unit_subsidy_cents integer NOT NULL CHECK (unit_subsidy_cents BETWEEN 0 AND unit_price_cents),
    PRIMARY KEY (order_id, position)
);
CREATE TABLE payments (
    order_id uuid PRIMARY KEY REFERENCES orders(id),
    method text NOT NULL CHECK (method IN ('subsidy','payroll','cash','card','pix')),
    amount_cents bigint NOT NULL CHECK (amount_cents >= 0),
    reference text
);
CREATE TABLE reversals (
    order_id uuid PRIMARY KEY REFERENCES orders(id), actor_id uuid NOT NULL REFERENCES staff_users(id),
    reason text NOT NULL, refund_reference text, reversed_at timestamptz NOT NULL
);
CREATE TABLE audit (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    company_id uuid NOT NULL REFERENCES companies(id), terminal_id uuid NOT NULL REFERENCES terminals(id),
    actor_id uuid NOT NULL REFERENCES staff_users(id), action text NOT NULL, target_id uuid,
    detail jsonb, created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE outbox (
    id uuid PRIMARY KEY, order_id uuid NOT NULL REFERENCES orders(id), position integer NOT NULL,
    type text NOT NULL, payload jsonb NOT NULL, status text NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending','delivered','failed')),
    attempts integer NOT NULL DEFAULT 0, next_attempt_at timestamptz NOT NULL DEFAULT now(),
    delivered_at timestamptz, error_code text,
    UNIQUE (order_id, position)
);
CREATE INDEX outbox_pending_schedule ON outbox (next_attempt_at, id) WHERE status = 'pending';
CREATE TABLE sandbox_deliveries (
    id uuid PRIMARY KEY, type text NOT NULL, received_at timestamptz NOT NULL DEFAULT now()
);
