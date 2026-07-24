export type FieldKind = 'string' | 'number' | 'bool' | 'enum'

export type SettingField = {
  key: string
  label: string
  kind: FieldKind
  hint?: string
  options?: { value: string; label: string }[]
  step?: number
  min?: number
  max?: number
}

export type SettingSection = {
  id: string
  title: string
  fields: SettingField[]
}

/** High-value OptionSettings fields for the form UI. */
export const SETTING_SECTIONS: SettingSection[] = [
  {
    id: 'server',
    title: '服务器信息',
    fields: [
      { key: 'ServerName', label: '服务器名称', kind: 'string' },
      { key: 'ServerDescription', label: '服务器描述', kind: 'string' },
      { key: 'ServerPassword', label: '进服密码', kind: 'string', hint: '留空表示公开' },
      { key: 'AdminPassword', label: '管理员密码', kind: 'string', hint: '需与管理工具 adminPassword 一致才能用 REST' },
      { key: 'ServerPlayerMaxNum', label: '最大玩家数', kind: 'number', min: 1, max: 32, step: 1 },
      { key: 'PublicPort', label: '公开端口', kind: 'number', min: 1, max: 65535, step: 1 },
      { key: 'PublicIP', label: '公开 IP', kind: 'string' },
      { key: 'Region', label: '地区标签', kind: 'string' },
    ],
  },
  {
    id: 'api',
    title: '远程管理',
    fields: [
      { key: 'RESTAPIEnabled', label: '启用 REST API', kind: 'bool' },
      { key: 'RESTAPIPort', label: 'REST API 端口', kind: 'number', min: 1, max: 65535, step: 1 },
      { key: 'RCONEnabled', label: '启用 RCON', kind: 'bool' },
      { key: 'RCONPort', label: 'RCON 端口', kind: 'number', min: 1, max: 65535, step: 1 },
      { key: 'bUseAuth', label: '启用认证', kind: 'bool' },
    ],
  },
  {
    id: 'rates',
    title: '倍率与难度',
    fields: [
      {
        key: 'Difficulty',
        label: '难度预设',
        kind: 'enum',
        hint: '选 None 时下方倍率才会生效',
        options: [
          { value: 'None', label: 'None（自定义倍率）' },
          { value: 'Casual', label: 'Casual' },
          { value: 'Normal', label: 'Normal' },
          { value: 'Hard', label: 'Hard' },
        ],
      },
      { key: 'DayTimeSpeedRate', label: '白天流速', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'NightTimeSpeedRate', label: '夜晚流速', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'ExpRate', label: '经验倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'PalCaptureRate', label: '捕获倍率', kind: 'number', min: 0.1, max: 2, step: 0.1 },
      { key: 'PalSpawnNumRate', label: '帕鲁刷新倍率', kind: 'number', min: 0.1, step: 0.1, hint: '过高会增加服务器负载' },
      { key: 'CollectionDropRate', label: '采集掉落倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'EnemyDropItemRate', label: '敌人掉落倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'WorkSpeedRate', label: '工作速度倍率', kind: 'number', min: 0.1, step: 0.1 },
    ],
  },
  {
    id: 'rules',
    title: '玩法规则',
    fields: [
      {
        key: 'DeathPenalty',
        label: '死亡惩罚',
        kind: 'enum',
        options: [
          { value: 'None', label: '无' },
          { value: 'Item', label: '掉落物品' },
          { value: 'ItemAndEquipment', label: '掉落物品与装备' },
          { value: 'All', label: '全部（含帕鲁）' },
        ],
      },
      { key: 'bIsPvP', label: 'PvP 模式', kind: 'bool' },
      { key: 'bEnablePlayerToPlayerDamage', label: '玩家互相伤害', kind: 'bool' },
      { key: 'bEnableFriendlyFire', label: '友军伤害', kind: 'bool' },
      { key: 'bEnableInvaderEnemy', label: '据点袭击', kind: 'bool', hint: '关闭可减轻部分性能压力' },
      { key: 'bEnableFastTravel', label: '快速旅行', kind: 'bool' },
      { key: 'GuildPlayerMaxNum', label: '公会人数上限', kind: 'number', min: 1, max: 100, step: 1 },
      { key: 'BaseCampMaxNum', label: '据点数量上限', kind: 'number', min: 1, step: 1 },
      { key: 'BaseCampWorkerMaxNum', label: '据点工作帕鲁上限', kind: 'number', min: 1, max: 50, step: 1 },
      { key: 'PalEggDefaultHatchingTime', label: '蛋孵化时间（小时）', kind: 'number', min: 0, step: 1 },
      { key: 'AutoSaveSpan', label: '自动保存间隔（秒）', kind: 'number', min: 30, step: 30 },
    ],
  },
]

export const ALL_FORM_KEYS = SETTING_SECTIONS.flatMap(s => s.fields.map(f => f.key))
