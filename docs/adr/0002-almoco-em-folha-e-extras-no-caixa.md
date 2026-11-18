# ADR 0002 · Almoço em folha e extras no caixa

- Data de decisão: 2026-11-18
- Status: aceita para o piloto

## Contexto

O funcionário precisa consultar gastos anteriores e distinguir o que será descontado em folha do que é pago no caixa. Um atendimento pode combinar almoço e extras, com subsídio por item. Os lançamentos já registrados também alimentam conciliação e estornos.

## Decisão

Gravar um pagamento por destino de cobrança. O valor líquido do almoço vai para folha conforme elegibilidade. O valor líquido dos extras é pago em dinheiro, cartão ou PIX. A cotação indica os dois valores e o servidor aplica essa regra na confirmação. O comprovante mostra cada parcela e o extrato usa pagamentos registrados e itens com preço histórico.

## Consequências

A migration amplia a chave de pagamentos para permitir dois métodos por pedido, sem reclassificar pagamentos anteriores. A fila de folha emite o valor da parcela registrada, inclusive na reversão. O extrato pertence ao atendimento identificado, usa o fuso comercial da unidade e exclui pedidos estornados. Se o almoço estiver totalmente subsidiado, a parcela em folha é zero.
