[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeJson,

    [string]$StaticJson = (Join-Path (Split-Path -Parent $PSScriptRoot) "analysis\item-catalog.json"),

    [string]$MarkdownOutput = (Join-Path (Split-Path -Parent $PSScriptRoot) "docs\ITEM_RUNTIME_CATALOG.md"),

    [string]$MergedJsonOutput = (Join-Path (Split-Path -Parent $PSScriptRoot) "analysis\item-catalog-runtime.json")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $RuntimeJson -PathType Leaf)) {
    throw "Runtime item catalog was not found: $RuntimeJson"
}

$catalog = Get-Content -LiteralPath $RuntimeJson -Raw | ConvertFrom-Json
$entries = @($catalog.entries)
$itemTypes = @($catalog.itemTypes)
$itemTypes2 = @($catalog.itemTypes2)
$itemBuffs = @($catalog.itemBuffs)
$itemEffects = @($catalog.itemEffects)
$itemUses = @($catalog.itemUses)
$itemPlaceEffects = @($catalog.itemPlaceEffects)
$itemConditions = @($catalog.itemConditions)
$languageX = @($catalog.languageX)
$staticCatalog = $null
if (Test-Path -LiteralPath $StaticJson -PathType Leaf) {
    $staticCatalog = Get-Content -LiteralPath $StaticJson -Raw | ConvertFrom-Json
}
$rootDirectory = Split-Path -Parent $PSScriptRoot
$outputDirectory = Split-Path -Parent $MarkdownOutput
$mdTick = [char]96

if ($null -ne $staticCatalog) {
    $mergedEntries = @(
        foreach ($entry in $entries) {
            $mergedEntry = [ordered]@{}
            foreach ($property in $entry.PSObject.Properties) {
                $mergedEntry[$property.Name] = $property.Value
            }

            $mergedEntry.staticNames = @(
                $staticCatalog.entries |
                    Where-Object id -eq $entry.id |
                    ForEach-Object name
            )
            [pscustomobject]$mergedEntry
        }
    )

    $mergedDocument = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        gameBuild = $catalog.gameBuild
        sources = @{
            runtime = $RuntimeJson
            static = $StaticJson
        }
        counts = @{
            runtimeEntries = $entries.Count
            staticConstants = $staticCatalog.counts.itemIdConstants
            uniqueStaticIds = $staticCatalog.counts.uniqueItemIds
            itemTypes = $itemTypes.Count
            itemTypes2 = $itemTypes2.Count
            itemBuffs = $itemBuffs.Count
            itemEffects = $itemEffects.Count
            itemUses = $itemUses.Count
            itemPlaceEffects = $itemPlaceEffects.Count
            itemConditions = $itemConditions.Count
            languageX = $languageX.Count
        }
        itemTypes = $itemTypes
        itemTypes2 = $itemTypes2
        itemBuffs = $itemBuffs
        itemEffects = $itemEffects
        itemUses = $itemUses
        itemPlaceEffects = $itemPlaceEffects
        itemConditions = $itemConditions
        languageX = $languageX
        entries = $mergedEntries
    }

    $mergedDirectory = Split-Path -Parent $MergedJsonOutput
    New-Item -ItemType Directory -Path $mergedDirectory -Force | Out-Null
    $mergedDocument | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $MergedJsonOutput -Encoding UTF8
}

function Add-Code([string]$value) {
    return $script:mdTick + $value + $script:mdTick
}

$typeNameById = @{}
foreach ($type in $itemTypes) {
    $typeNameById[[string]$type.id] = [string]$type.name
}
$effectDescriptionById = @{}
foreach ($effect in $itemEffects) {
    $effectDescriptionById[[string]$effect.id] = [string]$effect.description
}
$entryNameById = @{}
foreach ($entry in $entries) {
    $entryNameById[[string]$entry.id] = [string]$entry.name
}

function Resolve-TypeCode([object]$code) {
    $text = [string]$code
    if ([string]::IsNullOrWhiteSpace($text)) {
        return ""
    }

    $resolved = foreach ($part in ($text -split '_')) {
        $key = [string]$part
        if ($typeNameById.ContainsKey($key)) {
            $key + ":" + $typeNameById[$key]
        } else {
            $key
        }
    }
    return ($resolved -join " / ")
}

function Resolve-ItemName([object]$id) {
    $key = [string]$id
    if ($entryNameById.ContainsKey($key)) {
        return $entryNameById[$key]
    }
    return "ID $key"
}

function Resolve-EffectDescription([object]$id) {
    $key = [string]$id
    if ($effectDescriptionById.ContainsKey($key)) {
        return $effectDescriptionById[$key]
    }
    return "未知效果 $key"
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# 物品目录（运行时版）")
$lines.Add("")
$lines.Add("> 本文档由游戏运行时在 " + (Add-Code "GameplayReady") + " 后生成的 " + (Add-Code "item-runtime-catalog.json") + " 整理而来。")
$lines.Add(">")
$lines.Add("> 它反映当前存档/游戏版本实际加载到内存的配置，不修改物品、存档或角色数据。")
$lines.Add("")
$lines.Add("## 快照信息")
$lines.Add("")
$lines.Add("| 项目 | 值 |")
$lines.Add("|---|---|")
$lines.Add("| 游戏版本 | " + (Add-Code $catalog.gameBuild) + " |")
$lines.Add("| 捕获时间（UTC） | " + (Add-Code $catalog.capturedAtUtc) + " |")
$lines.Add("| 配置来源 | " + (Add-Code $catalog.source.configType) + " |")
$lines.Add("| 物品配置条数 | $($entries.Count) |")
$lines.Add("| 一级类型 | $($itemTypes.Count) |")
$lines.Add("| 二级类型 | $($itemTypes2.Count) |")
$lines.Add("| Buff 定义 | $($itemBuffs.Count) |")
$lines.Add("| 使用效果关联 | $($itemUses.Count) |")
$lines.Add("| 效果定义 | $($itemEffects.Count) |")
$lines.Add("| 放置效果关联 | $($itemPlaceEffects.Count) |")
$lines.Add("| 条件定义 | $($itemConditions.Count) |")
$lines.Add("| EX_language_x 条目 | $($languageX.Count) |")
$lines.Add("")
$lines.Add("## 这次读到了什么")
$lines.Add("")
$lines.Add("这次运行已经证明：")
$lines.Add("")
$lines.Add("- " + (Add-Code "ConfigData.singleton.item") + " 在 " + (Add-Code "GameplayReady") + " 时可访问。")
$lines.Add("- 实际运行时配置有 $($entries.Count) 条，比静态 " + (Add-Code "ItemID.cs") + " 的 284 个常量更多。")
$lines.Add("- " + (Add-Code "ConfigData.item_type") + " 已经成功解码为 $($itemTypes.Count) 个中文类型名；例如 " + (Add-Code "6_9") + " 可以解释为 " + (Add-Code "6:药品 / 9:材料") + "。")
$lines.Add("- " + (Add-Code "ConfigData.EX_itemuse") + " 与 " + (Add-Code "ConfigData.EX_itemeffect") + " 已建立 " + (Add-Code "itemId -> effectId -> 中文效果") + " 关联。")
$lines.Add("- 所有 $($entries.Count) 条配置都有可显示的中文名称。")
$lines.Add("- " + (Add-Code "ItemManage.GetItemName()") + " 能提供本地化名称。")
$lines.Add("- 当前快照中 " + (Add-Code "isItem=true") + " 有 $(($entries | Where-Object isItem).Count) 条，" + (Add-Code "isAttribute=true") + " 有 $(($entries | Where-Object isAttribute).Count) 条。")
$lines.Add("- 当前快照中装备有 $(($entries | Where-Object isEquipment).Count) 条，武器有 $(($entries | Where-Object isWeapon).Count) 条。")
$lines.Add("- 有 $(($entries | Where-Object { $_.sideEffectDescriptionKey }).Count) 条带副作用描述，$(($entries | Where-Object { $_.useLimitDescriptionKey }).Count) 条带使用限制描述。")
$lines.Add("- 有 $(($entries | Where-Object { $_.produceMaterial }).Count) 条带生产材料，说明生产配方可以从同一份 " + (Add-Code "c_item") + " 配置继续解析。")
$lines.Add("")
$lines.Add("## 代表物品")
$lines.Add("")
$lines.Add("以下条目用于验证（静态 ID → 运行时中文名 → 参数）的映射：")
$lines.Add("")
$lines.Add("| ID | 名称 | 类型码 | 价值 | 买价 | 堆叠 | 使用次数 | 参数 A | 参数 B | 装备 | 武器 |")
$lines.Add("|---:|---|---|---:|---:|---:|---:|---|---|:---:|:---:|")
$representativeIds = @(47, 48, 49, 54, 64, 69, 75, 76, 109, 131, 151, 165, 166, 219, 276, 360, 363, 421, 426)
foreach ($id in $representativeIds) {
    $entry = $entries | Where-Object id -eq $id | Select-Object -First 1
    if ($null -eq $entry) {
        continue
    }

    $lines.Add("| $($entry.id) | $($entry.name) | $(Add-Code $entry.itemType) | $($entry.itemValue) | $($entry.buyPrice) | $($entry.maxStack) | $($entry.maxUse) | $($entry.parameterA) | $($entry.parameterB) | $($entry.isEquipment) | $($entry.isWeapon) |")
}
$lines.Add("")
$lines.Add("## 已解码的一级类型")
$lines.Add("")
$lines.Add("类型表来自运行时 " + (Add-Code "ConfigData.item_type") + "，名称通过游戏的 " + (Add-Code "LanguageData.GetLanguage()") + " 解码；" + (Add-Code "equipmentType") + " 保留原始装备槽位编号。")
$lines.Add("")
$lines.Add("| ID | 中文名称 | equipmentType | 本地化 key |")
$lines.Add("|---:|---|---:|---|")
foreach ($type in ($itemTypes | Sort-Object id)) {
    $lines.Add("| $($type.id) | $($type.name) | $($type.equipmentType) | " + (Add-Code $type.nameKey) + " |")
}
$lines.Add("")
$lines.Add("## 已解码的二级类型")
$lines.Add("")
$lines.Add("| ID | 中文名称 | 本地化 key |")
$lines.Add("|---:|---|---|")
foreach ($type in ($itemTypes2 | Sort-Object id)) {
    $lines.Add("| $($type.id) | $($type.name) | " + (Add-Code $type.nameKey) + " |")
}
$lines.Add("")
$lines.Add("## 类型码分布")
$lines.Add("")
$lines.Add((Add-Code "itemType") + " 是游戏内部组合编码，例如 " + (Add-Code "6_9") + "。下面同时保留原始编码与按一级类型表翻译后的标签；下划线后的数字仍是游戏的组合字段，不擅自解释为第二张表。")
$lines.Add("")
$lines.Add("| 类型码 | 解码标签 | 条数 |")
$lines.Add("|---|---|---:|")
foreach ($group in ($entries | Group-Object itemType | Sort-Object Name)) {
    $lines.Add("| $(Add-Code $group.Name) | $(Add-Code (Resolve-TypeCode $group.Name)) | $($group.Count) |")
}
$lines.Add("")
$lines.Add("## 效果定义")
$lines.Add("")
$lines.Add((Add-Code "EX_itemeffect") + " 是效果字典；" + (Add-Code "functionType") + " 保留游戏原始值，中文描述来自运行时配置。")
$lines.Add("")
$lines.Add("| 效果 ID | functionType | 中文描述 |")
$lines.Add("|---:|---:|---|")
foreach ($effect in ($itemEffects | Sort-Object id)) {
    $lines.Add("| $($effect.id) | $($effect.functionType) | $($effect.description) |")
}
$lines.Add("")
$lines.Add("## 物品使用效果关联")
$lines.Add("")
$lines.Add("下表把 " + (Add-Code "EX_itemuse") + " 的效果 ID 连接到 " + (Add-Code "EX_itemeffect") + " 的中文描述，可用于判断吃、喝、服用等行为的实际数值变化。" + (Add-Code "valueType") + "、" + (Add-Code "timeType") + " 和随机字段仍保持原始值。")
$lines.Add("Mod 作者可以通过 " + (Add-Code "ItemCatalog.GetEffects(itemKey)") + " 读取这些关系；公共 API 返回命名空间效果键，不返回数字效果 ID。")
$lines.Add("")
$lines.Add("| 物品 ID | 物品 | 效果 ID | 效果 | 动作 | 值 | 参数 | 百分比 | 持续 | 时间类型 | 随机范围 |")
$lines.Add("|---:|---|---:|---|---|---:|---:|:---:|---:|---:|---|")
foreach ($use in ($itemUses | Sort-Object itemId, effectId)) {
    $randomRange = if (($use.randomMin -ne 0) -or ($use.randomMax -ne 0)) { "$($use.randomMin)-$($use.randomMax)" } else { "" }
    $lines.Add("| $($use.itemId) | $(Resolve-ItemName $use.itemId) | $($use.effectId) | $(Resolve-EffectDescription $use.effectId) | " + (Add-Code $use.action) + " | $($use.value) | $($use.valueParameter) | $($use.isPercent) | $($use.duration) | $($use.timeType) | " + (Add-Code $randomRange) + " |")
}
$lines.Add("")
$lines.Add("## Buff 定义")
$lines.Add("")
$lines.Add((Add-Code "itemBuff") + " 是物品附加 Buff 的原始关系；" + (Add-Code "function") + "、" + (Add-Code "type") + " 和 " + (Add-Code "parameter") + " 尚未做行为级推断，" + (Add-Code "display") + " 保留本地化 key。")
$lines.Add("")
$lines.Add("| 物品 ID | 物品 | function | type | parameter | display key |")
$lines.Add("|---:|---|---:|---:|---|---|")
foreach ($buff in ($itemBuffs | Sort-Object itemId, type, function)) {
    $lines.Add("| $($buff.itemId) | $(Resolve-ItemName $buff.itemId) | $($buff.function) | $($buff.type) | " + (Add-Code $buff.parameter) + " | " + (Add-Code $buff.display) + " |")
}
$lines.Add("")
$lines.Add("## 放置效果")
$lines.Add("")
$lines.Add((Add-Code "EX_itemPlaceEffect") + " 提供物品放置到场景/容器后的效果，共 $($itemPlaceEffects.Count) 条；效果 ID 已连接到中文效果字典。")
$lines.Add("")
$lines.Add("| 物品 ID | 物品 | 效果 ID | 效果 | 放置类型 | valueType | 参数 | 持续 | 时间类型 | 时间倍率 |")
$lines.Add("|---:|---|---:|---|---:|---:|---:|---:|---:|---:|")
foreach ($place in ($itemPlaceEffects | Sort-Object itemId, effectId, itemPlace)) {
    $lines.Add("| $($place.itemId) | $(Resolve-ItemName $place.itemId) | $($place.effectId) | $(Resolve-EffectDescription $place.effectId) | $($place.itemPlace) | $($place.valueType) | $($place.valueParameter) | $($place.duration) | $($place.timeType) | $($place.timeScale) |")
}
$lines.Add("")
$lines.Add("## 辅助配置")
$lines.Add("")
$lines.Add("- " + (Add-Code "EX_itemecondition") + " 当前快照为空（$($itemConditions.Count) 条），因此没有可列出的条件关系。")
$lines.Add("- " + (Add-Code "EX_language_x") + " 当前捕获 $($languageX.Count) 条；它是部分扩展参数表，不是完整语言字典。物品类型名称已经由游戏语言 API 直接解码。")
$lines.Add("")
$lines.Add("## 生产配方样例")
$lines.Add("")
$lines.Add((Add-Code "produceMaterial") + " 使用 " + (Add-Code "物品ID_数量,物品ID_数量") + " 格式；" + (Add-Code "produceNumber") + "、" + (Add-Code "produceTime") + "、" + (Add-Code "produceEnergy") + " 和 " + (Add-Code "produceType") + " 可以直接作为生产分析的第一层数据。")
$lines.Add("")
$lines.Add("| 输出 ID | 名称 | 材料 | 产出数 | 顺序 | 时间 | 类型 | 能量 |")
$lines.Add("|---:|---|---|---:|---:|---:|---:|---:|")
foreach ($entry in ($entries | Where-Object { $_.produceMaterial } | Select-Object -First 40)) {
    $lines.Add("| $($entry.id) | $($entry.name) | " + (Add-Code $entry.produceMaterial) + " | $($entry.produceNumber) | $($entry.produceOrder) | $($entry.produceTime) | $($entry.produceType) | $($entry.produceEnergy) |")
}
$lines.Add("")
$lines.Add("## 完整运行时列表")
$lines.Add("")
$lines.Add("| ID | 名称 | 类型码 | 类型2 | 价值 | 买价 | 堆叠 | 使用 | 体积 | 普通耐久 | 战斗耐久 | 骰子耐久 | 电池 | 装备 | 武器 | 参数 A | 参数 B |")
$lines.Add("|---:|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|:---:|:---:|---|---|")
foreach ($entry in ($entries | Sort-Object id, name)) {
    $lines.Add("| $($entry.id) | $($entry.name) | $(Add-Code $entry.itemType) | $(Add-Code $entry.itemType2) | $($entry.itemValue) | $($entry.buyPrice) | $($entry.maxStack) | $($entry.maxUse) | $($entry.volume) | $($entry.maxDurability) | $($entry.fightDurability) | $($entry.diceDurability) | $($entry.maxBattery) | $($entry.isEquipment) | $($entry.isWeapon) | $(Add-Code $entry.parameterA) | $(Add-Code $entry.parameterB) |")
}
$lines.Add("")
$lines.Add("## 解释边界")
$lines.Add("")
$lines.Add("- " + (Add-Code "parameterA") + "/" + (Add-Code "parameterB") + " 是原始字符串参数，格式可能是单个数值、下划线组合或其他物品引用；目前只记录，不推断其全部语义。")
$lines.Add("- itemType 的数字组合需要结合 c_item_type 表才能翻译成（食品/药品/装备）等稳定分类。")
$lines.Add("- " + (Add-Code "backgroundDescription") + " 是运行时读取到的文本；" + (Add-Code "itemDescription*Key") + "、" + (Add-Code "sideEffectDescriptionKey") + " 和 " + (Add-Code "useLimitDescriptionKey") + " 是本地化 key。")
$lines.Add("- 这份快照是当前游戏版本的只读数据；Mod 应通过 " + (Add-Code "ItemCatalog") + " 读取命名空间键，通过 " + (Add-Code "GameEvents") + " 观察行为。数字 ID 只应通过 " + (Add-Code "ItemIdResolver") + " 显式转换。")
$lines.Add("")
$lines.Add("## 下一步")
$lines.Add("")
$lines.Add("1. 读取 " + (Add-Code "ConfigData.item_type") + " 和 " + (Add-Code "item_type_2") + "，把类型码翻译成稳定名称。")
$lines.Add("2. 读取物品 Buff/效果配置，建立 " + (Add-Code "itemId -> effect") + " 关联。")
$lines.Add("3. 在 " + (Add-Code "PlayerItemUsedEvent") + " 前后采集玩家快照，验证参数与实际生命、心态、饱食等变化。")
$lines.Add("4. 使用只读 " + (Add-Code "ItemCatalog") + " API 和 " + (Add-Code "ItemCatalog.GetEffects(itemKey)") + "；只有底层调用需要整数时才使用 " + (Add-Code "ItemIdResolver") + " / " + (Add-Code "EffectIdResolver") + "。")

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$lines | Set-Content -LiteralPath $MarkdownOutput -Encoding UTF8
Write-Host "Generated $MarkdownOutput"
if ($null -ne $staticCatalog) {
    Write-Host "Generated $MergedJsonOutput"
}
