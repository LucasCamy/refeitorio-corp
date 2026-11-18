
# Checklist de segurança, privacidade e testes

## Piloto local

- [x] Seed fictício no sandbox e configuração sensível por ambiente.
- [x] Hash de senhas via PasswordHasher do ASP.NET Core e hash SHA-256 para crachá de alta entropia.
- [x] JWT de vida curta, validação de assinatura, emissor, público, validade e papéis. RF disponível somente em caixa autenticado.
- [x] SQL parametrizado, escopo de empresa/unidade, limitação de requisições e controle de concorrência.
- [x] Idempotência de confirmação, outbox e auditoria de identificação, conclusão, configuração e estorno.
- [x] Testes de domínio, contrato REST e cenários de integração com dados fictícios.

O `npm audit --omit=dev` e a verificação de pacotes NuGet não apontaram vulnerabilidades. A auditoria completa do npm encontrou um aviso no `picomatch`, dependência transitiva do Vite usada no desenvolvimento. O servidor de desenvolvimento escuta em loopback. [Aviso de segurança do picomatch](https://github.com/advisories/GHSA-fc5q-39xc-vjpf)

## Antes de operar com pessoas reais

- [ ] Definir controlador, operador, base legal, aviso e política de retenção com responsáveis da empresa. Não coletar foto, telefone ou dados bancários sem necessidade.
- [ ] Estabelecer revisão de acessos, revogação de crachá, rotação de chaves, mascaramento operacional e processo de atendimento ao titular. Anonimizar cadastros expirados conforme política de retenção e obrigações financeiras.
- [ ] Executar homologação do provedor de RH, credenciais de serviço, alertas e monitoramento de filas. Validar assinatura e replay se o fornecedor usar webhooks.
- [ ] Configurar TLS, segredos do ambiente, backup criptografado e restauração testada. Validar volume de carga, segurança de terminais e garantias do provedor externo.
- [ ] Homologar política de subsídio e limites, competências de folha, critérios para devolução, comprovante e conciliação.
