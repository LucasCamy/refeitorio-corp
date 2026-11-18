
export type Role = 'operator' | 'manager' | 'admin'
export type Staff = { name: string; role: Role; unitName: string; terminalName: string; timezone: string }
export type Session = { accessToken: string; expiresAt: string; staff: Staff; sandbox: boolean }
export type Category = 'beverage' | 'dessert' | 'meal' | 'other'
export type Product = {
  id: string; name: string; description: string; category: Category; barcode: string | null; imageKey: string | null
  priceCents: number; subsidyCents: number; available: boolean; version: number
}
export type MealRule = {
  name: string; description: string; startsAt: string; endsAt: string; priceCents: number; subsidyCents: number
  mealLimit: number; dailyLimitCents: number | null; monthlyLimitCents: number | null; version: number
}
export type Catalog = { products: Product[]; rule: MealRule; businessDate: string; timezone: string; mealOpen: boolean }
export type Employee = { id: string; rf: string; name: string; department: string; employment: string }
export type Identification = {
  employee: Employee; checkoutToken: string; expiresAt: string; payrollAllowed: boolean
  mealAvailable: boolean; mealMessage: string | null
}
export type Cart = { checkoutToken: string; includesMeal: boolean; items: { productId: string; quantity: number }[] }
export type Line = { productId: string | null; name: string; quantity: number; subtotalCents: number; subsidyCents: number }
export type Method = 'subsidy' | 'payroll' | 'cash' | 'card' | 'pix'
export type Quote = {
  items: Line[]; subtotalCents: number; subsidyCents: number; totalCents: number; version: string; payrollAllowed: boolean
  mealPayrollCents: number; extrasAtRegisterCents: number
}
export type Payment = { method: Method; amountCents: number; reference: string | null }
export type Receipt = Pick<Quote, 'items' | 'subtotalCents' | 'subsidyCents' | 'totalCents'> & {
  id: string; employee: Employee; createdAt: string; terminal: string; operator: string
  method: Method; status: 'confirmed' | 'reversed'; reference: string | null; payments: Payment[]
}
export type SpendingTotals = { totalCents: number; payrollCents: number; registerCents: number }
export type SpendingHistory = { unit: string; today: SpendingTotals; month: SpendingTotals; hasMore: boolean;
  extras: { orderId: string; position: number; name: string; quantity: number; netCents: number; createdAt: string; terminal: string }[] }
export type ReportOrder = Pick<Receipt, 'id' | 'terminal' | 'method' | 'createdAt' | 'status' | 'totalCents' | 'reference'> & { employee: string; paymentMethods: Method[] }
export type Report = {
  from: string; to: string; page: number
  summary: { orders: number; meals: number; subtotalCents: number; subsidyCents: number; totalCents: number; reversals: number }
  methods: { method: Method; orders: number; totalCents: number }[]; orders: ReportOrder[]
}
export type Integration = {
  mode: string; pending: number; delivered: number; failed: number
  events: { id: string; orderId: string; type: string; status: 'pending' | 'delivered' | 'failed'; attempts: number; deliveredAt: string | null; errorCode: string | null }[]
}

export class ApiError extends Error {
  constructor(readonly status: number, readonly code: string | undefined, message: string) { super(message) }
}
export type Request = <T>(path: string, options?: RequestInit) => Promise<T>

export async function request<T>(path: string, options: RequestInit = {}, token?: string): Promise<T> {
  const headers = new Headers(options.headers)
  if (options.body) headers.set('Content-Type', 'application/json')
  if (token) headers.set('Authorization', 'Bearer ' + token)
  const response = await fetch('/api/v1' + path, { ...options, headers, cache: 'no-store' })
  if (!response.ok) {
    const error = await response.json().catch(() => ({}))
    throw new ApiError(response.status, error.code, error.errors ? 'Confira os campos informados.' :
      error.title || 'Não foi possível concluir. Tente novamente.')
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>
}
export const body = (value: unknown, method = 'POST'): RequestInit => ({ method, body: JSON.stringify(value) })
export const message = (error: unknown) => error instanceof ApiError ? error.message : 'Falha de comunicação. Tente novamente.'
export const currency = (value: number) => new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(value / 100)
export const initials = (value: string) => value.trim().split(/\s+/).slice(0, 2).map(part => part[0]).join('').toUpperCase()
export const methods: Record<Method, string> = { subsidy: 'Subsídio integral', payroll: 'Desconto em folha', cash: 'Dinheiro', card: 'Cartão', pix: 'PIX' }
export const categories: Record<Category, string> = { beverage: 'Bebidas', dessert: 'Sobremesas', meal: 'Refeições', other: 'Outros' }
export const localTime = (value: string, timeZone: string) =>
  new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short', timeZone }).format(new Date(value))
