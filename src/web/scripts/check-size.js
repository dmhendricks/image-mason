// Fails when the interface's JavaScript, gzipped, passes its budget, so growing past it is a deliberate choice.
// Run after `npm run build`.
import { readdirSync, readFileSync } from 'node:fs'
import { gzipSync } from 'node:zlib'

const budget = 100 * 1024
const assets = new URL('../dist/assets/', import.meta.url)
const kb = bytes => `${(bytes / 1024).toFixed(1).padStart(7)} KB`

let total = 0
for (const name of readdirSync(assets).filter(name => name.endsWith('.js')).sort()) {
  const size = gzipSync(readFileSync(new URL(name, assets))).length
  total += size
  console.log(`${kb(size)}  ${name}`)
}

console.log(`${kb(total)}  total, gzipped (budget ${budget / 1024} KB)`)
if (total > budget) {
  console.error('The JavaScript is over budget.')
  process.exit(1)
}
