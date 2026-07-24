/**
 * Parse / serialize PalWorldSettings.ini OptionSettings=(Key=Value,...) block.
 */

const OPTION_SETTINGS_RE = /OptionSettings\s*=\s*\(/i

export type SettingsMap = Record<string, string>

export type ParseResult = {
  ok: boolean
  settings: SettingsMap
  /** Full file text used as base when rewriting */
  raw: string
  error?: string
}

/** Split OptionSettings inner content by commas, respecting quotes. */
export function splitOptionPairs(inner: string): string[] {
  const parts: string[] = []
  let buf = ''
  let inQuotes = false
  for (let i = 0; i < inner.length; i++) {
    const ch = inner[i]
    if (ch === '"') {
      // Unreal-style: "" inside quotes is escaped quote — keep simple: toggle on "
      inQuotes = !inQuotes
      buf += ch
      continue
    }
    if (ch === ',' && !inQuotes) {
      if (buf.trim()) parts.push(buf.trim())
      buf = ''
      continue
    }
    buf += ch
  }
  if (buf.trim()) parts.push(buf.trim())
  return parts
}

/** Find matching closing paren for OptionSettings=( starting at openParenIndex. */
export function findClosingParen(text: string, openParenIndex: number): number {
  let depth = 0
  let inQuotes = false
  for (let i = openParenIndex; i < text.length; i++) {
    const ch = text[i]
    if (ch === '"') {
      inQuotes = !inQuotes
      continue
    }
    if (inQuotes) continue
    if (ch === '(') depth++
    else if (ch === ')') {
      depth--
      if (depth === 0) return i
    }
  }
  return -1
}

export function parsePalWorldSettings(raw: string): ParseResult {
  const text = raw ?? ''
  const m = OPTION_SETTINGS_RE.exec(text)
  if (!m) {
    return {
      ok: false,
      settings: {},
      raw: text,
      error: '未找到 OptionSettings=(...)，请检查是否为有效的 PalWorldSettings.ini',
    }
  }

  const openParen = text.indexOf('(', m.index + m[0].length - 1)
  if (openParen < 0) {
    return { ok: false, settings: {}, raw: text, error: 'OptionSettings 缺少左括号' }
  }

  const closeParen = findClosingParen(text, openParen)
  if (closeParen < 0) {
    return { ok: false, settings: {}, raw: text, error: 'OptionSettings 括号不匹配' }
  }

  const inner = text.slice(openParen + 1, closeParen)
  const settings: SettingsMap = {}
  for (const pair of splitOptionPairs(inner)) {
    const eq = pair.indexOf('=')
    if (eq <= 0) continue
    const key = pair.slice(0, eq).trim()
    const value = pair.slice(eq + 1).trim()
    if (key) settings[key] = value
  }

  return { ok: true, settings, raw: text }
}

/** Strip surrounding quotes from a setting value for form display. */
export function unwrapValue(raw: string): string {
  if (raw.length >= 2 && raw.startsWith('"') && raw.endsWith('"'))
    return raw.slice(1, -1).replace(/""/g, '"')
  return raw
}

export function isTruthy(raw: string | undefined): boolean {
  if (!raw) return false
  const v = unwrapValue(raw).trim().toLowerCase()
  return v === 'true' || v === '1'
}

/** Format a form value back into OptionSettings token. */
export function formatSettingValue(
  value: string | number | boolean,
  kind: 'string' | 'number' | 'bool' | 'enum'
): string {
  if (kind === 'bool') {
    const on = typeof value === 'boolean' ? value : isTruthy(String(value))
    return on ? 'True' : 'False'
  }
  if (kind === 'number') {
    const n = typeof value === 'number' ? value : Number(String(value).trim())
    return formatNumberValue(n)
  }
  const s = String(value)
  if (kind === 'enum' && /^[A-Za-z0-9_]+$/.test(s)) return s
  return `"${s.replace(/"/g, '""')}"`
}

export function formatNumberValue(n: number): string {
  if (!Number.isFinite(n)) return '0.000000'
  if (Number.isInteger(n)) return String(n)
  return n.toFixed(6)
}

export function rewriteOptionSettings(raw: string, settings: SettingsMap): string {
  const text = raw ?? ''
  const m = OPTION_SETTINGS_RE.exec(text)
  if (!m) {
    const body = Object.entries(settings)
      .map(([k, v]) => `${k}=${v}`)
      .join(',')
    return `[/Script/Pal.PalGameWorldSettings]\r\nOptionSettings=(${body})\r\n`
  }

  const openParen = text.indexOf('(', m.index + m[0].length - 1)
  const closeParen = findClosingParen(text, openParen)
  if (openParen < 0 || closeParen < 0) {
    throw new Error('无法重写 OptionSettings：括号不匹配')
  }

  const parsed = parsePalWorldSettings(text)
  const order: string[] = []
  for (const key of Object.keys(parsed.settings)) {
    if (!(key in settings)) continue
    order.push(key)
  }
  for (const key of Object.keys(settings)) {
    if (!order.includes(key)) order.push(key)
  }

  const body = order.map(k => `${k}=${settings[k]}`).join(',')
  return text.slice(0, openParen + 1) + body + text.slice(closeParen)
}
