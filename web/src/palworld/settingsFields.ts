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

const deathPenaltyOptions = [
  { value: 'None', label: '无' },
  { value: 'Item', label: '掉落物品' },
  { value: 'ItemAndEquipment', label: '掉落物品与装备' },
  { value: 'All', label: '全部（含帕鲁）' },
]

const difficultyOptions = [
  { value: 'None', label: 'None（自定义倍率）' },
  { value: 'Casual', label: 'Casual' },
  { value: 'Normal', label: 'Normal' },
  { value: 'Hard', label: 'Hard' },
]

const randomizerOptions = [
  { value: 'None', label: 'None' },
  { value: 'Region', label: 'Region' },
  { value: 'All', label: 'All' },
]

const logFormatOptions = [
  { value: 'Text', label: 'Text' },
  { value: 'Json', label: 'Json' },
]

/**
 * Full OptionSettings catalog for Palworld 1.0 dedicated server (119 keys).
 * Grouped for the config form UI; unknown keys in a file still render separately.
 */
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
      { key: 'CoopPlayerMaxNum', label: '合作人数上限', kind: 'number', min: 1, max: 32, step: 1, hint: '专用服上通常影响有限' },
      { key: 'PublicPort', label: '公开端口', kind: 'number', min: 1, max: 65535, step: 1 },
      { key: 'PublicIP', label: '公开 IP', kind: 'string' },
      { key: 'Region', label: '地区标签', kind: 'string' },
      { key: 'BanListURL', label: '封禁列表 URL', kind: 'string' },
      {
        key: 'CrossplayPlatforms',
        label: '跨平台',
        kind: 'string',
        hint: '例如 (Steam,Xbox,PS5,Mac)',
      },
      { key: 'bUseAuth', label: '启用平台认证', kind: 'bool' },
      { key: 'bAllowClientMod', label: '允许客户端 Mod', kind: 'bool' },
      { key: 'bShowPlayerList', label: '显示玩家列表', kind: 'bool' },
      { key: 'bIsShowJoinLeftMessage', label: '进出服聊天提示', kind: 'bool' },
      { key: 'ChatPostLimitPerMinute', label: '每分钟聊天上限', kind: 'number', min: 0, step: 1 },
      { key: 'LogFormatType', label: '日志格式', kind: 'enum', options: logFormatOptions },
    ],
  },
  {
    id: 'api',
    title: '远程管理与存档',
    fields: [
      { key: 'RESTAPIEnabled', label: '启用 REST API', kind: 'bool' },
      { key: 'RESTAPIPort', label: 'REST API 端口', kind: 'number', min: 1, max: 65535, step: 1 },
      { key: 'RCONEnabled', label: '启用 RCON', kind: 'bool' },
      { key: 'RCONPort', label: 'RCON 端口', kind: 'number', min: 1, max: 65535, step: 1 },
      { key: 'AutoSaveSpan', label: '自动保存间隔（秒）', kind: 'number', min: 30, step: 30 },
      { key: 'bIsUseBackupSaveData', label: '使用备份存档', kind: 'bool' },
      { key: 'bAllowGlobalPalboxExport', label: '允许全球帕鲁箱导出', kind: 'bool' },
      { key: 'bAllowGlobalPalboxImport', label: '允许全球帕鲁箱导入', kind: 'bool', hint: '公开服建议关闭' },
    ],
  },
  {
    id: 'rates',
    title: '时间与成长倍率',
    fields: [
      {
        key: 'Difficulty',
        label: '难度预设',
        kind: 'enum',
        hint: '选 None 时下方倍率才会按自定义生效',
        options: difficultyOptions,
      },
      { key: 'DayTimeSpeedRate', label: '白天流速', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'NightTimeSpeedRate', label: '夜晚流速', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'ExpRate', label: '经验倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'PalCaptureRate', label: '捕获倍率', kind: 'number', min: 0.1, max: 2, step: 0.1 },
      { key: 'PalSpawnNumRate', label: '帕鲁刷新倍率', kind: 'number', min: 0.1, step: 0.1, hint: '过高会增加服务器负载' },
      { key: 'WorkSpeedRate', label: '工作速度倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'MonsterFarmActionSpeedRate', label: '牧场工作速度', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'PalEggDefaultHatchingTime', label: '蛋孵化时间（小时）', kind: 'number', min: 0, step: 1 },
      { key: 'RandomizerType', label: '随机化类型', kind: 'enum', options: randomizerOptions },
      { key: 'RandomizerSeed', label: '随机种子', kind: 'string' },
      { key: 'bIsRandomizerPalLevelRandom', label: '随机化帕鲁等级', kind: 'bool' },
      { key: 'DenyTechnologyList', label: '禁用科技列表', kind: 'string', hint: '逗号分隔科技 ID，可留空' },
    ],
  },
  {
    id: 'combat',
    title: '伤害与战斗',
    fields: [
      { key: 'PalDamageRateAttack', label: '帕鲁攻击倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'PalDamageRateDefense', label: '帕鲁承伤倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'PlayerDamageRateAttack', label: '玩家攻击倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'PlayerDamageRateDefense', label: '玩家承伤倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'BuildObjectDamageRate', label: '建筑受伤倍率', kind: 'number', min: 0, step: 0.1 },
      { key: 'BuildObjectDeteriorationDamageRate', label: '建筑老化倍率', kind: 'number', min: 0, step: 0.1 },
      { key: 'BuildObjectHpRate', label: '建筑生命倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'EquipmentDurabilityDamageRate', label: '装备耐久损耗倍率', kind: 'number', min: 0, step: 0.1 },
      { key: 'bEnableInvaderEnemy', label: '据点袭击', kind: 'bool', hint: '关闭可减轻部分性能压力' },
      { key: 'EnablePredatorBossPal', label: '掠食者 Boss', kind: 'bool' },
      { key: 'bAllowEnhanceStat_Health', label: '允许强化生命', kind: 'bool' },
      { key: 'bAllowEnhanceStat_Attack', label: '允许强化攻击', kind: 'bool' },
      { key: 'bAllowEnhanceStat_Stamina', label: '允许强化耐力', kind: 'bool' },
      { key: 'bAllowEnhanceStat_Weight', label: '允许强化负重', kind: 'bool' },
      { key: 'bAllowEnhanceStat_WorkSpeed', label: '允许强化工作速度', kind: 'bool' },
    ],
  },
  {
    id: 'survival',
    title: '生存与回复',
    fields: [
      { key: 'PlayerStomachDecreaceRate', label: '玩家饥饿消耗', kind: 'number', min: 0, step: 0.1 },
      { key: 'PlayerStaminaDecreaceRate', label: '玩家耐力消耗', kind: 'number', min: 0, step: 0.1 },
      { key: 'PlayerAutoHPRegeneRate', label: '玩家自动回血', kind: 'number', min: 0, step: 0.1 },
      { key: 'PlayerAutoHpRegeneRateInSleep', label: '玩家睡眠回血', kind: 'number', min: 0, step: 0.1 },
      { key: 'PalStomachDecreaceRate', label: '帕鲁饥饿消耗', kind: 'number', min: 0, step: 0.1 },
      { key: 'PalStaminaDecreaceRate', label: '帕鲁耐力消耗', kind: 'number', min: 0, step: 0.1 },
      { key: 'PalAutoHPRegeneRate', label: '帕鲁自动回血', kind: 'number', min: 0, step: 0.1 },
      { key: 'PalAutoHpRegeneRateInSleep', label: '帕鲁睡眠回血', kind: 'number', min: 0, step: 0.1 },
      { key: 'ItemWeightRate', label: '物品重量倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'ItemCorruptionMultiplier', label: '物品腐坏倍率', kind: 'number', min: 0, step: 0.1 },
      { key: 'BlockRespawnTime', label: '重生等待（秒）', kind: 'number', min: 0, step: 1 },
      { key: 'RespawnPenaltyDurationThreshold', label: '重生惩罚判定时长（秒）', kind: 'number', min: 0, step: 1 },
      { key: 'RespawnPenaltyTimeScale', label: '重生惩罚时间倍率', kind: 'number', min: 0, step: 0.1 },
    ],
  },
  {
    id: 'drops',
    title: '掉落与采集',
    fields: [
      { key: 'CollectionDropRate', label: '采集掉落倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'CollectionObjectHpRate', label: '采集物生命倍率', kind: 'number', min: 0.1, step: 0.1 },
      {
        key: 'CollectionObjectRespawnSpeedRate',
        label: '采集物刷新间隔倍率',
        kind: 'number',
        min: 0.1,
        step: 0.1,
        hint: '数值越小刷新越快',
      },
      { key: 'EnemyDropItemRate', label: '敌人掉落倍率', kind: 'number', min: 0.1, step: 0.1 },
      { key: 'DropItemMaxNum', label: '地面掉落物上限', kind: 'number', min: 0, step: 100 },
      { key: 'PhysicsActiveDropItemMaxNum', label: '物理活跃掉落物上限', kind: 'number', min: -1, step: 1, hint: '-1 表示不限制' },
      { key: 'DropItemMaxNum_UNKO', label: '特殊掉落物上限', kind: 'number', min: 0, step: 10 },
      { key: 'DropItemAliveMaxHours', label: '掉落物保留（小时）', kind: 'number', min: 0, step: 0.5 },
      { key: 'bActiveUNKO', label: '启用特殊掉落相关', kind: 'bool' },
      { key: 'SupplyDropSpan', label: '补给投放间隔（分钟）', kind: 'number', min: 0, step: 1 },
    ],
  },
  {
    id: 'rules',
    title: '玩法规则',
    fields: [
      { key: 'DeathPenalty', label: '死亡惩罚', kind: 'enum', options: deathPenaltyOptions },
      { key: 'bIsPvP', label: 'PvP 模式', kind: 'bool' },
      { key: 'bEnablePlayerToPlayerDamage', label: '玩家互相伤害', kind: 'bool' },
      { key: 'bEnableFriendlyFire', label: '友军伤害', kind: 'bool' },
      { key: 'bEnableFastTravel', label: '快速旅行', kind: 'bool' },
      { key: 'bEnableFastTravelOnlyBaseCamp', label: '仅据点快速旅行', kind: 'bool' },
      { key: 'bIsStartLocationSelectByMap', label: '地图选择出生点', kind: 'bool' },
      { key: 'bEnableNonLoginPenalty', label: '离线惩罚', kind: 'bool' },
      { key: 'bExistPlayerAfterLogout', label: '下线后保留角色实体', kind: 'bool' },
      { key: 'bCanPickupOtherGuildDeathPenaltyDrop', label: '可拾取他公会死亡掉落', kind: 'bool' },
      { key: 'bEnableDefenseOtherGuildPlayer', label: '防御其他公会玩家', kind: 'bool' },
      { key: 'bHardcore', label: '硬核模式', kind: 'bool' },
      { key: 'bPalLost', label: '帕鲁永久丢失', kind: 'bool' },
      { key: 'bCharacterRecreateInHardcore', label: '硬核可重建角色', kind: 'bool' },
      { key: 'bIsMultiplay', label: '多人相关标志', kind: 'bool' },
      { key: 'bBuildAreaLimit', label: '建造区域限制', kind: 'bool' },
      { key: 'MaxBuildingLimitNum', label: '每人建筑上限', kind: 'number', min: 0, step: 1, hint: '0 表示不限制' },
      { key: 'bDisplayPvPItemNumOnWorldMap_Player', label: '地图显示玩家 PvP 物品数', kind: 'bool' },
      { key: 'bDisplayPvPItemNumOnWorldMap_BaseCamp', label: '地图显示据点 PvP 物品数', kind: 'bool' },
      {
        key: 'bAdditionalDropItemWhenPlayerKillingInPvPMode',
        label: 'PvP 击杀额外掉落',
        kind: 'bool',
      },
      {
        key: 'AdditionalDropItemWhenPlayerKillingInPvPMode',
        label: 'PvP 击杀掉落物品',
        kind: 'string',
      },
      {
        key: 'AdditionalDropItemNumWhenPlayerKillingInPvPMode',
        label: 'PvP 击杀掉落数量',
        kind: 'number',
        min: 0,
        step: 1,
      },
    ],
  },
  {
    id: 'guild',
    title: '公会与据点',
    fields: [
      { key: 'GuildPlayerMaxNum', label: '公会人数上限', kind: 'number', min: 1, max: 100, step: 1 },
      { key: 'BaseCampMaxNum', label: '据点数量上限', kind: 'number', min: 1, step: 1 },
      { key: 'BaseCampMaxNumInGuild', label: '公会内据点上限', kind: 'number', min: 1, step: 1 },
      { key: 'BaseCampWorkerMaxNum', label: '据点工作帕鲁上限', kind: 'number', min: 1, max: 50, step: 1 },
      { key: 'bAutoResetGuildNoOnlinePlayers', label: '公会无人时自动重置', kind: 'bool' },
      { key: 'AutoResetGuildTimeNoOnlinePlayers', label: '公会重置时间（小时）', kind: 'number', min: 0, step: 1 },
      { key: 'GuildRejoinCooldownMinutes', label: '重新加入公会冷却（分钟）', kind: 'number', min: 0, step: 1 },
      { key: 'AutoTransferMasterThresholdDays', label: '会长自动移交天数', kind: 'number', min: 0, step: 1 },
      {
        key: 'AutoTransferMasterCheckIntervalSeconds',
        label: '会长移交检查间隔（秒）',
        kind: 'number',
        min: 0,
        step: 60,
      },
      { key: 'bInvisibleOtherGuildBaseCampAreaFX', label: '隐藏他公会据点区域特效', kind: 'bool' },
      { key: 'bEnableBuildingPlayerUIdDisplay', label: '显示建筑放置者', kind: 'bool' },
      { key: 'BuildingNameDisplayCacheTTLSeconds', label: '建筑名显示缓存（秒）', kind: 'number', min: 0, step: 1 },
    ],
  },
  {
    id: 'voice',
    title: '语音聊天',
    fields: [
      { key: 'bEnableVoiceChat', label: '启用近距离语音', kind: 'bool', hint: '专用服侧开关；部分构建可能不支持' },
      { key: 'VoiceChatMaxVolumeDistance', label: '满音量距离', kind: 'number', min: 0, step: 100 },
      { key: 'VoiceChatZeroVolumeDistance', label: '静音距离', kind: 'number', min: 0, step: 100 },
    ],
  },
  {
    id: 'perf',
    title: '性能与同步',
    fields: [
      { key: 'ServerReplicatePawnCullDistance', label: '同步裁剪距离', kind: 'number', min: 0, step: 100 },
      { key: 'MaxGuildsPerFrame', label: '每帧处理公会数', kind: 'number', min: 1, step: 1 },
      {
        key: 'ItemContainerForceMarkDirtyInterval',
        label: '物品容器脏标记间隔',
        kind: 'number',
        min: 0,
        step: 0.1,
      },
      {
        key: 'PlayerDataPalStorageUpdateCheckTickInterval',
        label: '帕鲁仓库检查间隔',
        kind: 'number',
        min: 0,
        step: 0.1,
      },
      { key: 'bEnableAimAssistPad', label: '手柄瞄准辅助', kind: 'bool' },
      { key: 'bEnableAimAssistKeyboard', label: '键鼠瞄准辅助', kind: 'bool' },
    ],
  },
]

export const ALL_FORM_KEYS = new Set(
  SETTING_SECTIONS.flatMap((s) => s.fields.map((f) => f.key))
)

export const FIELD_BY_KEY: Record<string, SettingField> = Object.fromEntries(
  SETTING_SECTIONS.flatMap((s) => s.fields.map((f) => [f.key, f]))
)

/** Infer control type for keys present in ini but not in the catalog. */
export function inferFieldKind(rawValue: string): FieldKind {
  const v = (rawValue ?? '').trim()
  if (/^(True|False)$/i.test(v)) return 'bool'
  if (/^-?\d+(\.\d+)?$/.test(v)) return 'number'
  return 'string'
}

export function makeUnknownField(key: string, rawValue = ''): SettingField {
  const kind = inferFieldKind(rawValue)
  return {
    key,
    label: key,
    kind,
    hint: '文件中存在、尚未归类的配置项',
  }
}
