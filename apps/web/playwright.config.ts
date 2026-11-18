
import { defineConfig, devices } from '@playwright/test'
import { readFileSync } from 'node:fs'

try {
  const config = readFileSync(new URL('../../.env', import.meta.url), 'utf8')
  process.env.E2E_PASSWORD ??= /^SANDBOX_PASSWORD=([a-f0-9]{32,64})\r?$/m.exec(config)?.[1]
} catch {
  // E2E_PASSWORD can be configured in the environment for an isolated sandbox.
}
if (!process.env.E2E_PASSWORD) throw new Error('Informe E2E_PASSWORD ou execute scripts/dev.ps1 setup.')

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  use: { baseURL: 'http://127.0.0.1:5173', ...devices['Desktop Chrome'], trace: 'off' },
  reporter: 'list'
})
