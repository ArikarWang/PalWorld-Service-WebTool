<template>
  <div>
    <div class="toolbar">
      <h1>配置文件</h1>
      <button class="btn" :disabled="busy" @click="load">重新加载</button>
      <button class="btn primary" :disabled="busy || !canSave" @click="save">保存</button>
    </div>
    <p class="hint">
      编辑 PalWorldSettings.ini。表单修改常用项；保存后通常需重启帕鲁服务器才会生效。
    </p>
    <p v-if="parseError" class="error">{{ parseError }}</p>

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
import { SETTING_SECTIONS } from '../palworld/settingsFields'
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
/** Working OptionSettings map (raw tokens including quotes). */
const settings = reactive<SettingsMap>({})
const formTouched = ref(false)

const canSave = computed(() => rawContent.value.trim().length > 0)

function displayValue(key: string): string {
  return unwrapValue(settings[key] ?? '')
}

function boolValue(key: string): boolean {
  return isTruthy(settings[key])
}

function applyParsed(text: string) {
  const result = parsePalWorldSettings(text)
  rawContent.value = text
  parseError.value = result.error || ''
  parsedOk.value = result.ok
  Object.keys(settings).forEach(k => delete settings[k])
  if (result.ok) {
    Object.assign(settings, result.settings)
  }
  formTouched.value = false
}

function onTextInput(key: string, value: string) {
  settings[key] = formatSettingValue(value, 'string')
  formTouched.value = true
}

function onNumberInput(key: string, value: string) {
  settings[key] = formatSettingValue(value === '' ? 0 : Number(value), 'number')
  formTouched.value = true
}

function onEnumInput(key: string, value: string) {
  settings[key] = formatSettingValue(value, 'enum')
  formTouched.value = true
}

function onBoolInput(key: string, checked: boolean) {
  settings[key] = formatSettingValue(checked, 'bool')
  formTouched.value = true
}

function onRawEdited() {
  // Re-parse when user edits raw text so form stays in sync if possible
  const result = parsePalWorldSettings(rawContent.value)
  parseError.value = result.error || ''
  parsedOk.value = result.ok
  if (result.ok) {
    Object.keys(settings).forEach(k => delete settings[k])
    Object.assign(settings, result.settings)
    formTouched.value = false
  }
}

async function load() {
  busy.value = true
  try {
    const res = await api.getConfig(id())
    applyParsed(res.content || '')
    toast('配置已加载')
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    busy.value = false
  }
}

async function save() {
  busy.value = true
  try {
    let toWrite = rawContent.value
    if (parsedOk.value) {
      toWrite = rewriteOptionSettings(rawContent.value, { ...settings })
      rawContent.value = toWrite
      formTouched.value = false
    }
    await api.putConfig(id(), toWrite)
    applyParsed(toWrite)
    toast('配置已保存')
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    busy.value = false
  }
}

onMounted(async () => {
  busy.value = true
  try {
    const res = await api.getConfig(id())
    applyParsed(res.content || '')
  } catch (e: any) {
    toast(e.message, 'error')
  } finally {
    busy.value = false
  }
})
</script>

<style scoped>
.form-grid {
  display: grid;
  gap: var(--space-3);
}

.field-row {
  display: grid;
  gap: 0.4rem;
  margin: 0;
  color: var(--text);
  font-size: 0.92rem;
}

.field-row.field-bool {
  grid-template-columns: 1fr auto;
  align-items: center;
  gap: var(--space-3);
}

.field-row.field-bool .field-hint {
  grid-column: 1 / -1;
}

.field-label {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 0.5rem;
  color: var(--muted);
  font-size: 0.85rem;
}

.field-label small {
  opacity: 0.75;
}

.field-hint {
  color: var(--muted);
  font-size: 0.8rem;
}

.field-row input[type="text"],
.field-row input[type="number"],
.field-row select {
  width: 100%;
  margin-top: 0;
  padding: 0.65rem 0.85rem;
  background: var(--bg);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  color: var(--text);
  font: inherit;
}

.field-row input[type="checkbox"] {
  width: 1.15rem;
  height: 1.15rem;
  accent-color: var(--primary);
}

.raw-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2);
  margin-bottom: var(--space-2);
}

.raw-head h3 {
  margin: 0;
}

@media (min-width: 840px) {
  .form-grid {
    grid-template-columns: 1fr 1fr;
  }

  .field-row.field-bool {
    grid-column: 1 / -1;
  }
}
</style>
