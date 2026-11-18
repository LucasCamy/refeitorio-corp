
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'

async function login(page: Page, user = 'admin') {
  await page.goto('/')
  await page.getByLabel('Usuário').fill(user)
  await page.getByLabel('Senha').fill(process.env.E2E_PASSWORD!)
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Frente de caixa' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Itens extras' })).toBeVisible()
}

const shots = process.env.E2E_SCREENSHOTS

test('caixa responsivo em sete larguras, fluxo por teclado e tema escuro', async ({ page }) => {
  const errors: string[] = []
  page.on('pageerror', error => errors.push(error.message))
  await page.setViewportSize({ width: 1440, height: 1000 })
  await login(page)
  await expect(page.getByText('Suco de laranja', { exact: true })).toBeVisible()
  await page.evaluate(() => document.fonts.ready.then(() => true))
  if (shots) await page.screenshot({ path: shots + '/caixa-desktop.png', animations: 'disabled' })

  await page.getByRole('button', { name: 'Bebidas' }).click()
  await expect(page.getByText('Pudim de leite', { exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: 'Todos' }).click()
  await page.getByRole('button', { name: 'Tema escuro' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  if (shots) await page.screenshot({ path: shots + '/caixa-escuro.png', animations: 'disabled' })
  await page.getByRole('button', { name: 'Tema claro' }).click()
  for (const width of [360, 390, 640, 768, 1024, 1440, 2560]) {
    await page.setViewportSize({ width, height: 1050 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width)
  }
  await page.setViewportSize({ width: 390, height: 844 })
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.getByLabel('Registro funcional').focus()
  await expect(page.getByLabel('Registro funcional')).toBeFocused()
  if (shots) await page.screenshot({ path: shots + '/caixa-mobile.png', animations: 'disabled', fullPage: true })
  for (const item of [
    { nav: 'Produtos', header: 'Produtos' },
    { nav: 'Regras', header: 'Regras de almoço' },
    { nav: 'Relatórios', header: 'Relatórios' },
    { nav: 'RH', header: 'Integração RH' }
  ]) {
    await page.getByRole('button', { name: item.nav, exact: true }).click()
    await expect(page.getByRole('heading', { name: item.header, exact: true })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
  }
  expect(errors).toEqual([])
})

test('RF, baixa, extra, PIX confirmado externamente, comprovante e estorno', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1050 })
  await login(page)
  await page.getByLabel('Registro funcional').fill('1002')
  await page.getByLabel('Registro funcional').press('Enter')
  await expect(page.getByText('Identificado', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Incluir almoço', exact: true }).click()
  await page.getByRole('button', { name: 'Adicionar Suco de laranja', exact: true }).click()
  await page.getByRole('button', { name: 'Revisar pedido' }).click()
  const payment = page.getByLabel('Pagamento dos extras')
  await expect(payment).toBeVisible()
  await expect(payment.locator('option[value=payroll]')).toHaveCount(0)
  await payment.selectOption('pix')
  await page.getByLabel('Referência da operação externa').fill('PIX-SANDBOX-TESTE')
  await page.getByLabel('Confirmei o pagamento no terminal ou app externo').check()
  await page.getByRole('button', { name: 'Confirmar atendimento', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Comprovante' })).toBeVisible()
  await expect(page.getByText('Atendimento concluído', { exact: true })).toBeVisible()
  await expect(page.getByText('PIX no caixa', { exact: true })).toBeVisible()
  if (shots) await page.screenshot({ path: shots + '/caixa-comprovante.png', animations: 'disabled' })

  await page.getByRole('button', { name: 'Novo atendimento' }).click()
  await page.getByLabel('Registro funcional').fill('1002')
  await page.getByLabel('Registro funcional').press('Enter')
  await page.setViewportSize({ width: 390, height: 844 })
  const spending = page.getByRole('button', { name: 'Consultar gastos' })
  await spending.focus()
  await page.keyboard.press('Enter')
  const history = page.getByRole('dialog', { name: 'Gastos do funcionário' })
  await expect(history.getByText('1 × Suco de laranja')).toBeVisible()
  await expect(history.getByText('Pago no caixa')).toHaveCount(2)
  await expect(history.getByText('Rafael Nunes')).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390)
  if (shots) await page.screenshot({ path: shots + '/caixa-gastos-mobile.png', animations: 'disabled' })
  await page.keyboard.press('Escape')
  await expect(history).toHaveCount(0)
  await expect(spending).toBeFocused()
  await page.getByRole('button', { name: 'Trocar' }).click()
  await page.setViewportSize({ width: 1440, height: 1050 })

  await page.getByRole('button', { name: 'Relatórios', exact: true }).click()
  const latest = page.getByRole('row').filter({ hasText: 'PIX-SANDBOX-TESTE' }).filter({ hasText: 'Registrado' }).first()
  await latest.getByRole('button', { name: 'Detalhes' }).click()
  await page.getByRole('button', { name: 'Estornar atendimento' }).click()
  await page.getByLabel('Motivo do estorno').fill('Validação de ponta a ponta em sandbox')
  await page.getByLabel('Referência da devolução').fill('DEV-SANDBOX-TESTE')
  await page.getByRole('button', { name: 'Confirmar estorno' }).click()
  await expect(page.getByText('Atendimento estornado', { exact: true })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
  await expect(latest).toHaveCount(0)
})
