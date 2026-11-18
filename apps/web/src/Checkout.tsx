
import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import {
  ArrowRight, Barcode, CircleAlert, CircleCheck, Clock3, CreditCard, CupSoda, Delete,
  HandCoins, History, IdCard, Minus, Plus, Printer, ReceiptText, RotateCcw, Search, ShoppingBasket,
  SquareX, UserRound, Utensils
} from 'lucide-react'
import { ApiError, body, categories, currency, initials, localTime, message, methods } from './api'
import type { Cart, Catalog, Category, Identification, Method, Product, Quote, Receipt, Request } from './api'
import { PageHeading } from './PageHeading'
import { SpendingDialog } from './SpendingDialog'

const filters: { id: Category | 'all'; label: string }[] = [
  { id: 'all', label: 'Todos' },
  ...Object.entries(categories).map(([id, label]) => ({ id: id as Category, label }))
]
const categoryOrder: Record<Category, number> = { beverage: 0, dessert: 1, meal: 2, other: 3 }

function PaymentSummary({ subtotal, subsidy, total, mealPayroll }: { subtotal: number; subsidy: number; total: number; mealPayroll?: number }) {
  return <dl className="totals">
    <div><dt>Subtotal</dt><dd>{currency(subtotal)}</dd></div>
    <div><dt>Subsídio da empresa</dt><dd className={subsidy ? 'success-text' : ''}>{subsidy ? '− ' : ''}{currency(subsidy)}</dd></div>
    {mealPayroll !== undefined && <div><dt>Almoço em folha</dt><dd>{currency(mealPayroll)}</dd></div>}
    <div className="total"><dt>{mealPayroll === undefined ? 'Total do atendimento' : 'Extras a pagar no caixa'}</dt>
      <dd>{currency(total - (mealPayroll ?? 0))}</dd></div>
  </dl>
}

export function ReceiptView({ receipt, timezone }: { receipt: Receipt; timezone: string }) {
  return <div className="receipt">
    <p className={receipt.status === 'reversed' ? 'status danger' : 'status success'}>
      {receipt.status === 'reversed' ? <SquareX size={16} /> : <CircleCheck size={16} />}
      {receipt.status === 'reversed' ? 'Atendimento estornado' : 'Atendimento concluído'}
    </p>
    <h2>Comprovante</h2>
    <p><strong>{receipt.employee.name}</strong><br />{receipt.terminal} · {localTime(receipt.createdAt, timezone)}</p>
    <ul className="receipt-lines">{receipt.items.map((line, index) =>
      <li key={line.productId ?? index}><div><span>{line.quantity} × {line.name}</span><small>{line.productId ? 'Extra' : 'Almoço'}</small></div>
        <strong>{currency(line.subtotalCents)}</strong></li>)}</ul>
    <PaymentSummary subtotal={receipt.subtotalCents} subsidy={receipt.subsidyCents} total={receipt.totalCents} />
    <ul className="receipt-lines payment-destinations" aria-label="Pagamentos do atendimento">
      {[...receipt.payments].sort((a, b) => Number(b.method === 'payroll') - Number(a.method === 'payroll')).map(payment =>
        <li key={payment.method}><div><strong>{payment.method === 'payroll' || payment.method === 'subsidy'
          ? methods[payment.method] : methods[payment.method] + ' no caixa'}</strong>
          {payment.reference && <small>Referência {payment.reference}</small>}</div><strong>{currency(payment.amountCents)}</strong></li>)}
    </ul>
    <p className="receipt-id">{receipt.id}</p>
    <button type="button" className="button secondary no-print" onClick={() => window.print()}><Printer size={16} /> Imprimir comprovante</button>
  </div>
}

export function Checkout({ api, timezone, active }: { api: Request; timezone: string; active: boolean }) {
  const [catalog, setCatalog] = useState<Catalog | null>(null)
  const [catalogError, setCatalogError] = useState('')
  const [identity, setIdentity] = useState<Identification | null>(null)
  const [spendingOpen, setSpendingOpen] = useState(false)
  const [mode, setMode] = useState<'rf' | 'badge'>('rf')
  const [rf, setRf] = useState('')
  const [category, setCategory] = useState<Category | 'all'>('all')
  const [search, setSearch] = useState('')
  const [items, setItems] = useState<Record<string, number>>({})
  const [meal, setMeal] = useState(false)
  const [quote, setQuote] = useState<Quote | null>(null)
  const [method, setMethod] = useState<Method>('cash')
  const [received, setReceived] = useState(false)
  const [reference, setReference] = useState('')
  const [receipt, setReceipt] = useState<Receipt | null>(null)
  const [working, setWorking] = useState(false)
  const [identifyError, setIdentifyError] = useState('')
  const [orderError, setOrderError] = useState('')
  const [retryPending, setRetryPending] = useState(false)
  const input = useRef<HTMLInputElement>(null)
  const pay = useRef<HTMLSelectElement>(null)
  const confirmAction = useRef<HTMLButtonElement>(null)
  const key = useRef<string | null>(null)
  const orderSection = useRef<HTMLElement>(null)
  const receiptStart = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!active) return
    const controller = new AbortController()
    api<Catalog>('/checkout/catalog', { signal: controller.signal })
      .then(value => { setCatalog(value); setCatalogError('') })
      .catch(error => { if (!controller.signal.aborted) setCatalogError(message(error)) })
    return () => controller.abort()
  }, [api, active])

  async function refresh() {
    setCatalogError('')
    try { setCatalog(await api<Catalog>('/checkout/catalog')) }
    catch (error) { setCatalogError(message(error)) }
  }

  function resetPayment() {
    setQuote(null)
    setOrderError('')
    setReceived(false)
    setReference('')
    key.current = null
  }

  async function identify(event: FormEvent) {
    event.preventDefault()
    if (!rf.trim() || working || identity || receipt) return
    setWorking(true)
    setIdentifyError('')
    try {
      const value = await api<Identification>('/checkout/identify', body({ kind: mode, value: rf.trim() }))
      setIdentity(value)
      setRf('')
    } catch (error) { setIdentifyError(message(error)); input.current?.select() }
    finally { setWorking(false) }
  }

  async function clear() {
    setWorking(true)
    setIdentifyError('')
    try {
      if (identity) await api('/checkout/sessions/' + identity.checkoutToken, { method: 'DELETE' })
      setIdentity(null); setItems({}); setMeal(false); setReceipt(null); setRetryPending(false); setRf('')
      resetPayment()
      requestAnimationFrame(() => input.current?.focus())
    } catch (error) { setIdentifyError(message(error)) }
    finally { setWorking(false) }
  }

  function add(product: Product, delta: number) {
    if (!identity || quote || working || receipt) return
    if (delta > 0 && !items[product.id] && Object.values(items).filter(Boolean).length >= 30) {
      setOrderError('O pedido aceita até 30 produtos diferentes.'); return
    }
    setItems(previous => ({ ...previous, [product.id]: Math.max(0, Math.min(20, (previous[product.id] ?? 0) + delta)) }))
    setOrderError('')
  }

  function cart(): Cart {
    return {
      checkoutToken: identity!.checkoutToken,
      includesMeal: meal,
      items: Object.entries(items).filter(([, quantity]) => quantity > 0).map(([productId, quantity]) => ({ productId, quantity }))
    }
  }

  async function review() {
    if (!identity || working) return
    setWorking(true)
    setOrderError('')
    try {
      const value = await api<Quote>('/checkout/quote', body(cart()))
      setMethod(value.extrasAtRegisterCents > 0 ? 'cash' : value.mealPayrollCents > 0 ? 'payroll' : 'subsidy')
      setQuote(value)
      key.current = crypto.randomUUID()
      requestAnimationFrame(() => (pay.current ?? confirmAction.current)?.focus())
    } catch (error) {
      if (error instanceof ApiError) reconcile(error)
      setOrderError(message(error))
    }
    finally { setWorking(false) }
  }

  function reconcile(error: ApiError) {
    if (error.code === 'product_unavailable') void refresh()
    if (['meal_closed', 'duplicate_meal', 'payroll_required'].includes(error.code ?? '')) {
      setMeal(false)
      setIdentity(current => current && { ...current, mealAvailable: false, mealMessage: error.message })
    }
  }

  async function confirm(event: FormEvent) {
    event.preventDefault()
    if (!identity || !quote || working || !key.current) return
    setWorking(true)
    setOrderError('')
    try {
      const init = body({ ...cart(), quoteVersion: quote.version, method, received, reference: reference || null })
      const result = await api<Receipt>('/orders', { ...init, headers: { 'Idempotency-Key': key.current } })
      setReceipt(result); setIdentity(null); setItems({}); setMeal(false); setRetryPending(false); resetPayment()
      requestAnimationFrame(() => receiptStart.current?.focus())
    } catch (error) {
      const uncertainty = !(error instanceof ApiError) || error.status >= 500 && error.code !== 'hr_unavailable' ||
        retryPending && error.status === 429
      setRetryPending(uncertainty)
      if (error instanceof ApiError && ['quote_changed', 'product_unavailable', 'meal_closed', 'duplicate_meal', 'spending_limit', 'payroll_required'].includes(error.code ?? '')) {
        resetPayment()
        reconcile(error)
        if (error.code === 'quote_changed') void refresh()
      }
      setOrderError(uncertainty ? 'A confirmação pode ter sido registrada. Tente recuperar o mesmo comprovante.' : message(error))
    } finally { setWorking(false) }
  }

  const selected = catalog?.products.filter(product => (items[product.id] ?? 0) > 0) ?? []
  const count = (meal ? 1 : 0) + Object.values(items).reduce((sum, quantity) => sum + quantity, 0)
  const calculatedSubtotal = (meal ? catalog?.rule.priceCents ?? 0 : 0) + selected.reduce((sum, p) => sum + p.priceCents * items[p.id], 0)
  const calculatedSubsidy = (meal ? catalog?.rule.subsidyCents ?? 0 : 0) + selected.reduce((sum, p) => sum + p.subsidyCents * items[p.id], 0)
  const mealPayroll = meal && catalog ? catalog.rule.priceCents - catalog.rule.subsidyCents : 0
  const canEdit = !!identity && !quote && !working
  const visible = catalog?.products.filter(p => (category === 'all' || p.category === category) &&
    (p.name.toLocaleLowerCase('pt-BR').includes(search.toLocaleLowerCase('pt-BR').trim()) || p.barcode === search.trim()))
    .sort((a, b) => categoryOrder[a.category] - categoryOrder[b.category] || a.name.localeCompare(b.name, 'pt-BR')) ?? []

  return <>
    <PageHeading title="Frente de caixa" subtitle="Registre refeições e itens extras dos funcionários.">
      {catalog && <span className="date-chip"><Clock3 size={16} />{new Date(catalog.businessDate + 'T12:00:00').toLocaleDateString('pt-BR', { day: 'numeric', month: 'long', year: 'numeric' })}</span>}
    </PageHeading>
    {catalogError && <div className="notice error" role="alert"><CircleAlert size={18} />{catalogError}<button className="button secondary" onClick={refresh}>Tentar novamente</button></div>}
    <div className="pos-grid">
      <div className="pos-primary">
        <section className="panel identify">
          <div className="panel-lead"><span className="section-icon"><IdCard size={19} /></span>
            <div><h2>Identificar funcionário</h2><p>Busque pelo RF ou use o leitor de crachá.</p></div></div>
          {identity ? <div className="person">
            <span className="avatar large">{initials(identity.employee.name)}</span><div className="person-info"><strong>{identity.employee.name}</strong>
              <small>RF {identity.employee.rf} · {identity.employee.department} · {identity.employee.employment}</small></div>
            <span className="status success"><CircleCheck size={15} /> Identificado</span>
            <div className="person-actions">
              <button className="button secondary compact" type="button" aria-haspopup="dialog"
                onClick={() => setSpendingOpen(true)} disabled={working || retryPending}><History size={16} /> Consultar gastos</button>
              <button className="button secondary compact" onClick={clear} disabled={working || retryPending}>Trocar</button>
            </div>
          </div> : <form className="identify-form" onSubmit={identify}>
            <div className="segmented" aria-label="Forma de identificação">
              <button type="button" className={mode === 'rf' ? 'chosen' : ''} aria-pressed={mode === 'rf'}
                onClick={() => { setMode('rf'); setRf(''); setIdentifyError(''); input.current?.focus() }}><IdCard size={15} /> RF</button>
              <button type="button" className={mode === 'badge' ? 'chosen' : ''} aria-pressed={mode === 'badge'}
                onClick={() => { setMode('badge'); setRf(''); setIdentifyError(''); input.current?.focus() }}><Barcode size={16} /> Crachá</button>
            </div>
            <label className="search-field"><Search size={17} /><span className="sr-only">{mode === 'rf' ? 'Registro funcional' : 'Código do crachá'}</span>
              <input ref={input} aria-invalid={!!identifyError} aria-describedby={identifyError ? 'identify-error' : undefined} value={rf} onChange={e => setRf(e.target.value)} required maxLength={mode === 'rf' ? 32 : 128}
                placeholder={mode === 'rf' ? 'Digite o RF' : 'Leia o crachá'} disabled={!!receipt} />
            </label>
            <button className="button primary" disabled={working || !!receipt}>{working && !identity ? 'Buscando…' : 'Buscar'}<ArrowRight size={16} /></button>
          </form>}
          {identifyError && <p className="error" id="identify-error" role="alert"><CircleAlert size={16} />{identifyError}</p>}
        </section>

        {catalog && <section className="meal-banner">
          <div className="meal-copy">
            <span className="meal-kicker"><Utensils size={14} /> Almoço do dia</span>
            <h2>{catalog.rule.name}</h2><p>{catalog.rule.description}</p>
            <div className="meal-meta"><strong>{currency(catalog.rule.priceCents)}</strong>
              <span>{catalog.rule.subsidyCents === catalog.rule.priceCents ? 'Subsídio integral' : 'Subsídio de ' + currency(catalog.rule.subsidyCents)}</span></div>
          </div>
          <img src="/menu/lunch.webp" alt="" width={480} height={480} />
          <div className="meal-actions">
            {identity?.mealMessage && <small><CircleAlert size={15} />{identity.mealMessage}</small>}
            {!identity && !catalog.mealOpen && <small><Clock3 size={15} />Fora do horário</small>}
            {identity?.mealAvailable && <button className="button meal-button" type="button" disabled={!canEdit} aria-pressed={meal} onClick={() => { setMeal(!meal); setOrderError('') }}>
              {meal ? <CircleCheck size={16} /> : <Plus size={17} />}{meal ? 'Almoço incluído' : 'Incluir almoço'}
            </button>}
          </div>
        </section>}

        <section className="catalog">
          <div className="catalog-heading"><div><h2>Itens extras</h2><p>Adicione bebidas, sobremesas e refeições ao pedido.</p></div>
            <label className="search-field product-search"><Search size={16} /><span className="sr-only">Buscar produto</span>
              <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Buscar produto" /></label></div>
          <div className="filter-row" aria-label="Categorias">
            {filters.filter(f => f.id !== 'other' || catalog?.products.some(p => p.category === 'other')).map(filter =>
              <button className={'filter ' + (category === filter.id ? 'chosen' : '')} key={filter.id} type="button"
                aria-pressed={category === filter.id} onClick={() => setCategory(filter.id)}>{filter.label}</button>)}
          </div>
          {!catalog && !catalogError && <div className="product-grid" role="status" aria-label="Carregando catálogo">
            {[1, 2, 3].map(i => <div key={i} className="skeleton product-placeholder" />)}</div>}
          {catalog && !visible.length && <div className="empty small"><Search size={25} /><strong>Nenhum produto encontrado</strong>
            <button className="button secondary" onClick={() => { setSearch(''); setCategory('all') }}>Limpar filtros</button></div>}
          <div className="product-grid">{visible.map(product => <article className="product-card" key={product.id}>
            {product.imageKey ? <img src={'/menu/' + product.imageKey + '.webp'} alt="" width={480} height={480} /> :
              <div className="product-image"><CupSoda size={26} /></div>}
            <div className="product-details"><span className="product-kind">{categories[product.category]}</span><h3>{product.name}</h3>
              {product.description && <p>{product.description}</p>}
              <div className="product-price"><strong>{currency(product.priceCents)}</strong>
                <button type="button" className="add-button" aria-label={'Adicionar ' + product.name} disabled={!canEdit || (items[product.id] ?? 0) >= 20}
                  onClick={() => add(product, 1)}><Plus size={18} /></button></div>
            </div>
          </article>)}</div>
        </section>
      </div>

      <aside className="panel order" ref={orderSection} aria-label="Pedido atual">
        {receipt ? <div tabIndex={-1} ref={receiptStart}><ReceiptView receipt={receipt} timezone={timezone} />
          <button className="button primary order-action no-print" onClick={clear} disabled={working}>Novo atendimento<ArrowRight size={17} /></button></div> :
          <>
            <div className="order-heading"><span className="section-icon"><ShoppingBasket size={19} /></span><h2>Pedido atual</h2><span className="count">{count}</span></div>
            {identity ? <div className="order-person"><UserRound size={16} /><strong>{identity.employee.name}</strong><span>RF {identity.employee.rf}</span></div> :
              <div className="order-person"><UserRound size={16} /><span>Funcionário não identificado</span></div>}
            {quote ? <ul className="receipt-lines">{quote.items.map((line, index) =>
              <li key={line.productId ?? index}><span>{line.quantity} × {line.name}</span><strong>{currency(line.subtotalCents)}</strong></li>)}</ul> :
              count === 0 ? <div className="empty order-empty">
              <span className="empty-icon"><ReceiptText size={25} /></span><strong>Nenhum item no pedido</strong><p>{identity ? 'Inclua o almoço ou adicione um extra.' : 'Identifique o funcionário para começar.'}</p>
            </div> : <ul className="cart-lines">
              {meal && catalog && <li><div><span className="line-name">{catalog.rule.name}</span><small>Almoço · 1 unidade</small></div>
                <strong>{currency(catalog.rule.priceCents)}</strong>
                {!quote && <button className="icon-button" aria-label="Remover almoço" type="button" disabled={working} onClick={() => setMeal(false)}><Delete size={17} /></button>}</li>}
              {selected.map(product => <li key={product.id}><div><span className="line-name">{product.name}</span><small>{currency(product.priceCents)} / un.</small></div>
                <strong>{currency(product.priceCents * items[product.id])}</strong>
                <div className="quantity"><button type="button" className="icon-button" aria-label={'Remover uma unidade de ' + product.name} disabled={!canEdit} onClick={() => add(product, -1)}><Minus size={14} /></button>
                  <span>{items[product.id]}</span><button type="button" className="icon-button" aria-label={'Adicionar uma unidade de ' + product.name}
                    disabled={!canEdit || items[product.id] >= 20} onClick={() => add(product, 1)}><Plus size={14} /></button></div></li>)}
              {selected.length !== Object.values(items).filter(Boolean).length &&
                <li><span>Alguns produtos estão indisponíveis.</span><button className="button secondary" onClick={() => setItems(Object.fromEntries(selected.map(p => [p.id, items[p.id]])))}>Remover</button></li>}
            </ul>}
            <PaymentSummary subtotal={quote?.subtotalCents ?? calculatedSubtotal} subsidy={quote?.subsidyCents ?? calculatedSubsidy}
              total={quote?.totalCents ?? calculatedSubtotal - calculatedSubsidy} mealPayroll={quote?.mealPayrollCents ?? mealPayroll} />
            {orderError && <p className="error" role="alert"><CircleAlert size={16} />{orderError}</p>}
            {quote ? <form className="form-stack payment-form" onSubmit={confirm}>
              {quote.extrasAtRegisterCents > 0 && <label>Pagamento dos extras<select ref={pay} value={method} disabled={working || retryPending}
                onChange={e => { setMethod(e.target.value as Method); setReceived(false); setReference('') }}>
                <option value="cash">{methods.cash}</option><option value="card">{methods.card}</option><option value="pix">{methods.pix}</option>
              </select></label>}
              {quote.extrasAtRegisterCents === 0 && <p>{quote.mealPayrollCents > 0 ? 'O almoço será lançado em folha.' : 'Atendimento coberto pelo subsídio da empresa.'}</p>}
              {(method === 'card' || method === 'pix') && <label>Referência da operação externa
                <input required maxLength={100} placeholder={method === 'card' ? 'NSU da operação' : 'ID da transação PIX'}
                  value={reference} disabled={working || retryPending} onChange={e => setReference(e.target.value)} /></label>}
              {(['cash', 'card', 'pix'] as Method[]).includes(method) && <label className="check-row">
                <input type="checkbox" required checked={received} disabled={working || retryPending} onChange={e => setReceived(e.target.checked)} />
                {method === 'cash' ? 'Confirmei o recebimento em dinheiro' : 'Confirmei o pagamento no terminal ou app externo'}
              </label>}
              <button ref={confirmAction} className="button primary order-action" disabled={working}><CreditCard size={17} />
                {working ? 'Confirmando…' : retryPending ? 'Recuperar comprovante' : 'Confirmar atendimento'}</button>
              {!retryPending && <button type="button" className="text-button" onClick={resetPayment} disabled={working}><RotateCcw size={14} /> Voltar ao pedido</button>}
            </form> : <button type="button" className="button primary order-action" onClick={review}
              disabled={!identity || !count || working || !catalog || selected.length !== Object.values(items).filter(Boolean).length}>
              {working && identity ? 'Consultando…' : 'Revisar pedido'}<ArrowRight size={17} /></button>}
            {!quote && <p className="order-foot"><HandCoins size={15} /> Pagamento na revisão do pedido</p>}
          </>}
      </aside>
    </div>
    <button className="mobile-order button primary" type="button" hidden={!identity || !count}
      onClick={() => { orderSection.current?.scrollIntoView({ block: 'start' }); orderSection.current?.querySelector<HTMLButtonElement>('.order-action:not(:disabled)')?.focus({ preventScroll: true }) }}>
      <ShoppingBasket size={17} /> Ver pedido
    </button>
    {identity && spendingOpen && <SpendingDialog api={api} identity={identity} timezone={timezone} close={() => setSpendingOpen(false)} />}
  </>
}
