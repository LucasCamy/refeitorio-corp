
# Arquitetura e modelo de dados

```mermaid
flowchart LR
    Pessoa["Operador ou administrador"] --> Web["React · caixa e gestão"]
    Web -->|REST /api/v1 + JWT| API["ASP.NET Core · contratos e RBAC"]
    API --> Regra["Domínio · data comercial, preços e limites"]
    API --> RH["Adaptador de elegibilidade do RH"]
    Regra --> PG[("PostgreSQL · pedidos, auditoria e outbox")]
    PG --> Worker["Processador de outbox"]
    Worker --> RH
    RH --> Sandbox["Sandbox local"]
    RH -. fornecedor a homologar .-> Provedor["HTTPS · OAuth 2.0"]
```

```mermaid
erDiagram
    COMPANY ||--o{ UNIT : possui
    COMPANY ||--o{ EMPLOYEE : possui
    COMPANY ||--o{ PRODUCT : cadastra
    COMPANY ||--|| MEAL_RULE : define
    UNIT ||--o{ TERMINAL : possui
    TERMINAL ||--o{ STAFF_USER : autoriza
    EMPLOYEE ||--o{ BADGE : identifica
    EMPLOYEE ||--o| CHECKOUT_SESSION : reserva
    EMPLOYEE ||--o{ ORDER : consome
    STAFF_USER ||--o{ ORDER : registra
    ORDER ||--|{ ORDER_ITEM : detalha
    ORDER ||--|{ PAYMENT : liquida
    ORDER ||--o| REVERSAL : estorna
    ORDER ||--o{ OUTBOX : publica
    STAFF_USER ||--o{ AUDIT : executa
```

As [migrations](../database/migrations) definem chaves e invariantes. O checkout bloqueia a linha do funcionário antes de verificar sessões, limites e as baixas do dia. Registros guardam preço e subsídio de cada item, operador, caixa, data comercial, instante e parcelas de pagamento. A associação empresa/unidade vem das credenciais de usuário. O worker retém somente o identificador externo do funcionário e os valores/itens indispensáveis para entrega ao RH.
