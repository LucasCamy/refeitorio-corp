
INSERT INTO companies (id,name,timezone) VALUES ('10000000-0000-4000-8000-000000000001','Refeitório Inteligente','America/Cuiaba') ON CONFLICT DO NOTHING;
INSERT INTO units (id,company_id,name) VALUES ('20000000-0000-4000-8000-000000000001','10000000-0000-4000-8000-000000000001','Unidade piloto') ON CONFLICT DO NOTHING;
INSERT INTO terminals (id,unit_id,name) VALUES
    ('30000000-0000-4000-8000-000000000001','20000000-0000-4000-8000-000000000001','Caixa 01'),
    ('30000000-0000-4000-8000-000000000002','20000000-0000-4000-8000-000000000001','Caixa 02')
ON CONFLICT DO NOTHING;
INSERT INTO employees (id,company_id,rf,name,department,employment,external_id,active,eligible,payroll_allowed) VALUES
    ('40000000-0000-4000-8000-000000000001','10000000-0000-4000-8000-000000000001','1001','Marina Costa','Operações','CLT','sandbox-1001',true,true,true),
    ('40000000-0000-4000-8000-000000000002','10000000-0000-4000-8000-000000000001','1002','Rafael Nunes','Logística','CLT','sandbox-1002',true,true,true),
    ('40000000-0000-4000-8000-000000000003','10000000-0000-4000-8000-000000000001','1003','Camila Santos','Facilities','CLT','sandbox-1003',false,false,false),
    ('40000000-0000-4000-8000-000000000004','10000000-0000-4000-8000-000000000001','1004','João Lima','Manutenção','Terceirizado','sandbox-1004',true,true,false)
ON CONFLICT DO NOTHING;
INSERT INTO badges (company_id,employee_id,token_hash) VALUES
    ('10000000-0000-4000-8000-000000000001','40000000-0000-4000-8000-000000000001',
        sha256(convert_to('RF-BADGE-77a64bab-fb5b-449b-9e48-931c02355b80','UTF8')))
ON CONFLICT DO NOTHING;
INSERT INTO products (id,company_id,name,description,category,price_cents,subsidy_cents,barcode,image_key,available) VALUES
    ('50000000-0000-4000-8000-000000000001','10000000-0000-4000-8000-000000000001','Suco de laranja','Natural, 300 ml','beverage',650,0,'7891000000011','juice',true),
    ('50000000-0000-4000-8000-000000000002','10000000-0000-4000-8000-000000000001','Refrigerante cola','Copo, 350 ml','beverage',550,0,'7891000000028','cola',true),
    ('50000000-0000-4000-8000-000000000003','10000000-0000-4000-8000-000000000001','Pudim de leite','Com calda de caramelo','dessert',600,0,'7891000000035','flan',true),
    ('50000000-0000-4000-8000-000000000004','10000000-0000-4000-8000-000000000001','Brownie de chocolate','Porção individual','dessert',500,0,'7891000000042','brownie',true),
    ('50000000-0000-4000-8000-000000000005','10000000-0000-4000-8000-000000000001','Marmitex adicional','Frango, arroz, feijão e salada','meal',1900,0,'7891000000059','takeout',true)
ON CONFLICT DO NOTHING;
INSERT INTO meal_rules (company_id,name,description,starts_at,ends_at,price_cents,subsidy_cents,meal_limit,daily_limit_cents,monthly_limit_cents)
VALUES ('10000000-0000-4000-8000-000000000001','Refeição do dia','Arroz, feijão, frango grelhado e acompanhamentos.',
    '00:00','23:59:59.999999',1800,1800,1,6000,40000)
ON CONFLICT DO NOTHING;
