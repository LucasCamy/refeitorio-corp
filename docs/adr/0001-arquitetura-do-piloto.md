
# ADR 0001 · Arquitetura do piloto

- Data de decisão: 2026-11-18
- Status: aceita para o piloto

## Contexto

O caixa precisa calcular o pedido, garantir a baixa única sob concorrência e continuar registrando a entrega à folha mesmo quando o RH estiver indisponível. O fornecedor de RH ainda será escolhido. O piloto terá uma unidade, com possibilidade de expansão.

## Decisão

Usar React, ASP.NET Core e PostgreSQL. Manter um monólito modular, com domínio, persistência e RH separados no código. Versionar o SQL e usar transações PostgreSQL para pedido, pagamento, auditoria e outbox. Adotar um adaptador de RH substituível com sandbox local. O caixa registra dinheiro, cartão e PIX confirmados pelo operador, além de desconto em folha entregue ao adaptador. Todos os montantes são inteiros em centavos.

## Consequências

O caixa depende de acesso ao servidor e ao PostgreSQL. A validação do RH falha de forma explícita quando a consulta externa estiver indisponível. O outbox separa confirmação do pedido e entrega à folha. A entrega externa é pelo menos uma vez e exige deduplicação do consumidor. Expansão exige provisionar unidades, caixas, usuários e regras e validar o escopo por empresa.
