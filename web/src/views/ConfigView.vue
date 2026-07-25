<template>
  <div>
    <div class="toolbar">
      <h1>配置文件</h1>
      <button class="btn" :disabled="busy" @click="load">重新加载</button>
      <button class="btn primary" :disabled="busy || !canSave" @click="save">保存</button>
    </div>
    <p class="hint">
      编辑 PalWorldSettings.ini。下方按 Palworld 1.0 专用服 OptionSettings 全量键（119 项）分组；
      文件里多出的键会显示在「文件中的额外项」。斑马纹便于对照每一行（含复选框）。
      保存后通常需重启帕鲁服务器才会生效。
    </p>
    <p v-if="parseError" class="error">{{ parseError }}</p>
    <p v-if="parsedOk" class="meta">
      已解析 {{ Object.keys(settings).length }} 项 · 表单收录 {{ knownCount }} 项
      <span v-if="unknownFields.length"> · 额外 {{ unknownFields.length }} 项</span>
    </p>

    <template v-if="parsedOk">
      <div
        v-for="section in sections"
        :key="section.id"
        class="panel"
      >
        <h3>{{ section.title }}</h3>
        <div class="form-grid">
          <label
            v-for="field in section.fields"
            :key="field.key"
            class="field-row"
            :class="{ 'field-bool': field.kind === 'bool' }"
          >
            <span class="field-label">
              {{ field.label }}
              <small class="mono">{{ field.key }}</small>
            </span>

            <input
              v-if="field.kind === 'string'"
              type="text"
              :value="displayValue(field.key)"
              @input="onTextInput(field.key, ($event.target as HTMLInputElement).value)"
            />

            <input
              v-else-if="field.kind === 'number'"
              type="number"
              :step="field.step ?? 1"
              :min="field.min"
              :max="field.max"
              :value="displayValue(field.key)"
              @input="onNumberInput(field.key, ($event.target as HTMLInputElement).value)"
            />

            <select
              v-else-if="field.kind === 'enum'"
              :value="displayValue(field.key)"
              @change="onEnumInput(field.key, ($event.target as HTMLSelectElement).value)"
            >
              <option
                v-for="opt in field.options || []"
                :key="opt.value"
                :value="opt.value"
              >{{ opt.label }}</option>
              <option
                v-if="displayValue(field.key) && !(field.options || []).some(o => o.value === displayValue(field.key))"
                :value="displayValue(field.key)"
              >{{ displayValue(field.key) }}（当前）</option>
            </select>

            <input
              v-else-if="field.kind === 'bool'"
              type="checkbox"
              :checked="boolValue(field.key)"
              @change="onBoolInput(field.key, ($event.target as HTMLInputElement).checked)"
            />

            <span v-if="field.hint" class="field-hint">{{ field.hint }}</span>
          </label>
        </div>
      </div>

      <div v-if="unknownFields.length" class="panel">
        <h3>文件中的额外项</h3>
        <p class="meta" style="margin-bottom: var(--space-3)">
          这些键存在于当前 ini，但尚未列入上方分组。仍可在此修改，保存时会一并写回。
        </p>
        <div class="form-grid">
          <label
            v-for="field in unknownFields"
            :key="field.key"
            class="field-row"
            :class="{ 'field-bool': field.kind === 'bool' }"
          >
            <span class="field-label">
              {{ field.label }}
              <small class="mono">{{ field.key }}</small>
            </span>

            <input
              v-if="field.kind === 'string'"
              type="text"
              :value="displayValue(field.key)"
              @input="onTextInput(field.key, ($event.target as HTMLInputElement).value)"
            />

            <input
              v-else-if="field.kind === 'number'"
              type="number"
              step="any"
              :value="displayValue(field.key)"
              @input="onNumberInput(field.key, ($event.target as HTMLInputElement).value)"
            />

            <input
              v-else-if="field.kind === 'bool'"
              type="checkbox"
              :checked="boolValue(field.key)"
              @change="onBoolInput(field.key, ($event.target as HTMLInputElement).checked)"
            />
          </label>
        </div>
      </div>
    </template>

    <div class="panel">
      <div class="raw-head">
        <h3>原始配置文本</h3>
        <button class="btn ghost sm" type="button" @click="showRaw = !showRaw">
          {{ showRaw ? '收起' : '展开' }}
        </button>
      </div>
      <p class="meta" style="margin-bottom: var(--space-3)">
        可直接编辑完整 ini。若与表单同时修改，保存时以<strong>表单优先合并进原文</strong>；解析失败时则按原始文本整文件保存。
      </p>
      <textarea
        v-show="showRaw || !parsedOk"
        v-model="rawContent"
        class="code-box"
        rows="16"
        @change="onRawEdited"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, inject, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { api } from '../api'
import {
  ALL_FORM_KEYS,
  makeUnknownField,
  SETTING_SECTIONS,
  type SettingField,
} from '../palworld/settingsFields'
import {
  formatSettingValue,
  isTruthy,
  parsePalWorldSettings,
  rewriteOptionSettings,
  unwrapValue,
  type SettingsMap,
} from '../utils/palWorldSettings'

const route = useRoute()
const id = () => route.params.id as string
const toast = inject<(m: string, t?: string) => void>('toast', () => {})

const sections = SETTING_SECTIONS
const busy = ref(false)
const showRaw = ref(false)
const rawContent = ref('')
const parseError = ref('')
const parsedOk = ref(false)
const settings = reactive<SettingsMap>({})
const formDirty = ref(false)

const knownCount = ALL_FORM_KEYS.size

const unknownFields = computed<SettingField[]>(() => {
  const keys = Object.keys(settings)
    .filter((k) => !ALL_FORM_KEYS.has(k))
    .sort((a, b) => a.localeCompare(b))
  return keys.map((k) => makeUnknownField(k, unwrapValue(settings[k] ?? '')))
})

const canSave = computed(() => !!rawContent.value || formDirty.value)

function clearSettings() {
  for (const k of Object.keys(settings)) delete settings[k]
}

function applyParsed(content: string) {
  const parsed = parsePalWorldSettings(content)
  clearSettings()
  Object.assign(settings, parsed.settings)
  parsedOk.value = parsed.ok
  parseError.value = parsed.error || ''
  formDirty.value = false
  if (!parsed.ok) showRaw.value = true
}

function displayValue(key: string): string {
  const raw = settings[key]
  if (raw === undefined || raw === null) return ''
  return unwrapValue(raw)
}

function boolValue(key: string): boolean {
  return isTruthy(settings[key] ?? 'False')
}

function setSetting(key: string, value: string) {
  settings[key] = value
  formDirty.value = true
}

function onTextInput(key: string, value: string) {
  setSetting(key, value)
}

function onNumberInput(key: string, value: string) {
  if (value === '' || value === '-') {
    delete settings[key]
    formDirty.value = true
    return
  }
  setSetting(key, value)
}

function onEnumInput(key: string, value: string) {
  setSetting(key, value)
}

function onBoolInput(key: string, checked: boolean) {
  setSetting(key, checked ? 'True' : 'False')
}

function onRawEdited() {
  applyParsed(rawContent.value)
}

async function load() {
  busy.value = true
  try {
    const res = await api.getConfig(id())
    rawContent.value = res.content || ''
    applyParsed(rawContent.value)
  } catch (e: any) {
    toast(e.message || String(e), 'error')
  } finally {
    busy.value = false
  }
}

async function save() {
  busy.value = true
  try {
    let content = rawContent.value
    if (parsedOk.value) {
      const map: SettingsMap = {}
      for (const [k, v] of Object.entries(settings)) {
        map[k] = formatSettingValue(k, v)
      }
      content = rewriteOptionSettings(rawContent.value, map)
      rawContent.value = content
    }
    await api.putConfig(id(), content)
    applyParsed(content)
    toast('配置已保存', 'success')
  } catch (e: any) {
    toast(e.message || String(e), 'error')
  } finally {
    busy.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.raw-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-3);
  margin-bottom: var(--space-2);
}

.raw-head h3 {
  margin: 0;
}

.form-grid {
  display: flex;
  flex-direction: column;
  gap: 0;
  border: 1px solid var(--border);
  border-radius: 10px;
  overflow: hidden;
}

.field-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(140px, 280px);
  align-items: center;
  gap: 10px 16px;
  margin: 0;
  padding: 12px 14px;
  min-width: 0;
  border-bottom: 1px solid var(--border);
  background: var(--surface);
}

.field-row:last-child {
  border-bottom: none;
}

.field-row:nth-child(even) {
  background: color-mix(in srgb, var(--surface2) 88%, var(--surface));
}

.field-row:hover {
  background: color-mix(in srgb, var(--accent) 8%, var(--surface));
}

.field-row.field-bool {
  grid-template-columns: minmax(0, 1fr) 28px;
}

.field-row.field-bool input[type='checkbox'] {
  width: 18px;
  height: 18px;
  margin: 0;
  justify-self: end;
  accent-color: var(--accent);
}

.field-row > input[type='text'],
.field-row > input[type='number'],
.field-row > select {
  width: 100%;
  min-width: 0;
}

.field-row .field-hint {
  grid-column: 1 / -1;
  margin-top: -4px;
}

.field-label {
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--text);
  min-width: 0;
}

.field-label small {
  font-weight: 500;
  font-size: 0.72rem;
  color: var(--muted);
  word-break: break-all;
}

.field-hint {
  font-size: 0.78rem;
  color: var(--muted);
  line-height: 1.4;
}

@media (max-width: 700px) {
  .field-row {
    grid-template-columns: 1fr;
    align-items: stretch;
  }

  .field-row.field-bool {
    grid-template-columns: minmax(0, 1fr) 28px;
    align-items: center;
  }
}

.code-box {
  width: 100%;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 0.82rem;
  line-height: 1.45;
  resize: vertical;
  min-height: 220px;
}
</style>
