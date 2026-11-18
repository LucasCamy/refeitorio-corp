ALTER TABLE payments DROP CONSTRAINT payments_pkey, ADD PRIMARY KEY (order_id, method);
