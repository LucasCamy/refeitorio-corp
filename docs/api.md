
# API interna e integração com RH

O contrato OpenAPI da aplicação roda em `/openapi/v1.json`. O Swagger UI local está em `/swagger`. As rotas de negócio usam `/api/v1`. O cliente envia JWT Bearer após login. Erros usam `application/problem+json`; conflitos de negócio podem incluir `code`. Valores usam centavos de BRL.

| Método e rota | Acesso | Resultado |
|---|---|---|
| `POST /auth/login` | Público, com limite de tentativas | Recebe `username` e `password`; devolve JWT de 45 min e o caixa vinculado. |
| `GET /checkout/catalog` | Caixa | Catálogo disponível, regra, data comercial e fuso. |
| `POST /checkout/identify` | Caixa | Recebe `{ "kind": "rf", "value": "1001" }` ou `kind: "badge"`. Retorna funcionário, elegibilidade e reserva de 5 minutos. |
| `DELETE /checkout/sessions/{token}` | Dono da reserva | Libera a reserva associada. |
| `GET /checkout/sessions/{token}/spending?page=1` | Dono da reserva ativa | Totais líquidos de hoje e do mês na unidade, discriminados por destino de pagamento. Lista extras confirmados do mês em páginas de 20 linhas. |
| `POST /checkout/quote` | Dono da reserva | Recebe `checkoutToken`, `includesMeal` e `items[{productId,quantity}]`. Devolve valores, versão, `mealPayrollCents` e `extrasAtRegisterCents`. |
| `POST /orders` | Dono da reserva | Mesmo carrinho com `quoteVersion`, `method`, `received` e `reference`. `method` é o meio de caixa quando há extras a cobrar, `payroll` quando só há almoço em folha ou `subsidy` quando o valor final é zero. Exige `Idempotency-Key: <uuid>`. |
| `GET /orders/{id}` | Operador titular ou gestão da unidade | Comprovante, parcelas efetivamente registradas em `payments` e estado corrente. Os campos `method` e `reference` representam o pagamento de caixa quando houver duas parcelas. |
| `POST /orders/{id}/reverse` | Gestão da unidade | Motivo e, quando aplicável, `refundReference`. Preserva pedido e grava ajuste. |
| `GET /products`, `POST /products`, `PUT /products/{id}` | Admin | Consulta, criação e atualização com a versão vigente. |
| `PUT /meal-rule` | Admin | Valida e atualiza preço, subsídio, janela e limites com versão. |
| `GET /reports/orders?from=YYYY-MM-DD&to=YYYY-MM-DD&page=1` | Gestão da unidade | Resumo, pagamentos e lista de 30 pedidos por página. Aceita até 93 dias. |
| `GET /integration/events`, `POST /integration/events/{id}/retry` | Admin | Consulta a fila da unidade e reprocessa eventos em falha. |

Exemplo mínimo de cotação do almoço após identificação:

```json
{
  "checkoutToken": "<uuid retornado pela identificação>",
  "includesMeal": true,
  "items": []
}
```

A confirmação compara a cotação ao estado atual do banco. O mesmo corpo e chave do mesmo operador devolvem o comprovante já registrado. Reutilizar a chave com conteúdo diferente gera `409 idempotency_conflict`. Uma chave nova requer uma reserva válida.

## Contrato outbound do adaptador de referência

O [diagrama de arquitetura](arquitetura.md) mostra as duas implementações. `Sandbox` valida contra fixtures e registra entregas idempotentes em banco. `External` precisa de homologação com um fornecedor. O perfil e as permissões locais são o cadastro-base. O adaptador confirma status, identificador externo e benefício no RH nas etapas críticas.

| Método do fornecedor | Entrada esperada | Saída esperada |
|---|---|---|
| `POST <Hr__TokenUrl>` | Formulário `grant_type=client_credentials`, `client_id`, `client_secret` e `scope` opcional | `access_token` e `expires_in` |
| `GET /employees/{rf}` | RF sanitizado, token Bearer | `{ "active": true, "externalId": "sandbox-1001" }` ou 404 |
| `GET /employees/{rf}/eligibility?date=2026-11-18&shift=LUNCH` | Data comercial ISO e turno | `{ "allowed": true, "payrollAllowed": true }` ou 404 |
| `POST /meal-entries` | Baixa referente à linha de almoço | HTTP 2xx após aplicação ou deduplicação |
| `POST /consumptions` | Linhas extras do pedido | HTTP 2xx após aplicação ou deduplicação |
| `POST /payroll-deductions` | Valor da parcela em folha, correspondente ao almoço líquido em novos pedidos | HTTP 2xx após aplicação ou deduplicação |

Os três POSTs esperam `Idempotency-Key` exclusivo por evento, sem alteração entre tentativas. `transactionId` correlaciona todos os eventos de um pedido. A compensação por estorno usa nova chave, `reversal: true` e valores negativos no nível da operação. As linhas discriminam quantidades e valores unitários originais. O payload inclui `employeeExternalId`, `date`, `shift`, `totalCents`, `subsidyCents` e `items`. Em folha, a lista de itens é vazia.

HTTP usa HTTPS, cliente com timeout de 4 s e sem redirecionamento. A fila usa backoff exponencial com até 8 tentativas automáticas e envio ordenado por pedido. Um evento em falha bloqueia os subsequentes desse pedido até reprocessamento. O consumidor precisa deduplicar por `Idempotency-Key` inclusive após perder a resposta. A tela de integração mostra código de erro e tentativas, sem expor payload nem token.

## Operações propostas para homologação

| Operação | Contrato a negociar |
|---|---|
| `GET /employees/{rf}/balance` | Centavos disponíveis, moeda e instante de atualização. Depende da adoção de pré-pago. |
| `POST /sync/employees` | Sincronização idempotente por lote, com cursor e escopo de empresa. |
| `POST /webhooks/employee-updated` | Atualização por evento autenticado, ID externo único, data e rejeição de replay. |

Sync, webhook, saldo e provisionamento do cadastro externo estão fora do sandbox atual. A troca de adaptador exige validar CPF/RF exigidos pelo fornecedor, identificadores estáveis, assinatura ou OAuth, capacidade de reversão, competência de folha e limites de taxa antes do piloto com dados reais.
