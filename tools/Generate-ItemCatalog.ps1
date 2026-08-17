[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ItemIdSource,

    [string]$JsonOutput = (Join-Path (Split-Path -Parent $PSScriptRoot) "analysis\item-catalog.json"),

    [string]$MarkdownOutput = (Join-Path (Split-Path -Parent $PSScriptRoot) "docs\ITEM_CATALOG.md")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ItemIdSource -PathType Leaf)) {
    throw "ItemID source was not found: $ItemIdSource"
}

$rootDirectory = Split-Path -Parent $PSScriptRoot
$sourceText = Get-Content -LiteralPath $ItemIdSource -Raw
$pattern = 'public\s+const\s+int\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?<id>-?\d+)'
$matches = [regex]::Matches($sourceText, $pattern)

if ($matches.Count -eq 0) {
    throw "No ItemID constants were found in $ItemIdSource"
}

$entries = @(
    foreach ($match in $matches) {
        [pscustomobject][ordered]@{
            name = $match.Groups["name"].Value
            id = [int]$match.Groups["id"].Value
        }
    }
)

$duplicateGroups = @(
    $entries |
        Group-Object -Property id |
        Where-Object Count -gt 1 |
        Sort-Object { [int]$_.Name } |
        ForEach-Object {
            [pscustomobject][ordered]@{
                id = [int]$_.Name
                aliases = @($_.Group | ForEach-Object name)
            }
        }
)

$runtimeFields = @(
    @{ name = "item_id"; type = "int"; group = "identity" },
    @{ name = "item_type"; type = "string"; group = "classification" },
    @{ name = "item_type_2"; type = "string"; group = "classification" },
    @{ name = "interactive_type"; type = "string"; group = "interaction" },
    @{ name = "item_value"; type = "int"; group = "economy" },
    @{ name = "buy_item_price"; type = "int"; group = "economy" },
    @{ name = "contraband"; type = "int"; group = "economy" },
    @{ name = "tradable"; type = "int"; group = "economy" },
    @{ name = "treasure"; type = "int"; group = "economy" },
    @{ name = "max_stack"; type = "int"; group = "stacking" },
    @{ name = "max_use"; type = "int"; group = "usage" },
    @{ name = "batch_use"; type = "int"; group = "usage" },
    @{ name = "max_durability"; type = "int"; group = "durability" },
    @{ name = "fight_durability"; type = "int"; group = "durability" },
    @{ name = "dice_durability"; type = "int"; group = "durability" },
    @{ name = "work_durability"; type = "int"; group = "durability" },
    @{ name = "max_battery"; type = "int"; group = "battery" },
    @{ name = "battery_p"; type = "int"; group = "battery" },
    @{ name = "volume"; type = "int"; group = "inventory" },
    @{ name = "maintenance"; type = "string"; group = "maintenance" },
    @{ name = "feedable"; type = "int"; group = "interaction" },
    @{ name = "gift_opinion"; type = "int"; group = "social" },
    @{ name = "side_effect_p"; type = "string"; group = "effect" },
    @{ name = "use_limit_p"; type = "string"; group = "effect" },
    @{ name = "exhaust_get_item"; type = "string"; group = "effect" },
    @{ name = "produce_material"; type = "string"; group = "production" },
    @{ name = "produce_number"; type = "int"; group = "production" },
    @{ name = "produce_order"; type = "int"; group = "production" },
    @{ name = "produce_time"; type = "int"; group = "production" },
    @{ name = "produce_type"; type = "int"; group = "production" },
    @{ name = "produce_energy"; type = "int"; group = "production" },
    @{ name = "produce_leader"; type = "string"; group = "production" },
    @{ name = "produce_unlock_1"; type = "string"; group = "production" },
    @{ name = "item_p_A"; type = "string"; group = "parameters" },
    @{ name = "item_p_B"; type = "string"; group = "parameters" },
    @{ name = "item_p_A_weaken"; type = "string"; group = "parameters" },
    @{ name = "item_p_B_weaken"; type = "string"; group = "parameters" },
    @{ name = "handbook_type"; type = "int"; group = "presentation" },
    @{ name = "item_pic_demo"; type = "string"; group = "presentation" },
    @{ name = "key_to_object"; type = "string"; group = "presentation" },
    @{ name = "L_item_name"; type = "string"; group = "localization" },
    @{ name = "L_item_des_A"; type = "string"; group = "localization" },
    @{ name = "L_item_des_B"; type = "string"; group = "localization" },
    @{ name = "L_background_des"; type = "string"; group = "localization" },
    @{ name = "L_side_effect_des"; type = "string"; group = "localization" },
    @{ name = "L_use_limit_des"; type = "string"; group = "localization" },
    @{ name = "L_battery_des"; type = "string"; group = "localization" }
)

$extensionMethods = @(
    "GetName", "GetTipsName", "GetItemType2", "GetTypeDesc", "GetDesc",
    "GetBuyPrice", "GetBuyRealPrice", "GetContraband", "GetEquipmentPlaceId",
    "GetCostDurability", "GetCostDurabilityDefault", "GetCostDurabilityByBattle",
    "GetCostDurabilityByThrowDice", "GetItemPARealTime", "GetItemPBRealTime",
    "GetOnceProduceNumber", "GetProduceMaterial", "GetNeedBatteryInfo",
    "GetFinalCostBattery", "IsHaveUseLimit", "IsHaveAlcohol", "IsHaveSideEffect",
    "IsContraband", "IsCanBeGift", "IsEquipment", "IsHaveBackgroundDes",
    "IsHaveDurability", "IsHaveBattery", "IsHaveUseTimes", "IsCanStack",
    "IsTreasure", "IsMedicine", "IsFood", "IsAlcohol", "IsFullGrid",
    "IsPackageConsumeItem", "IsItemFuncAOpen", "IsItemFuncBOpen",
    "IsShowSpecialItemGetTips", "IsTaskItem", "IsNeedYellowTipsItem",
    "IsHaveItemTitle", "IsBillItem", "IsKeyItem", "IsExtraPocketItem",
    "IsCanBeDestory"
)

$catalog = [ordered]@{
    schemaVersion = 1
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    game = [ordered]@{
        name = "Back To The Dawn"
        buildId = "23125213"
        unity = "2020.3.2f1c1"
        backend = "IL2CPP"
    }
    evidence = [ordered]@{
        itemIdSource = "analysis/AssetRipperProject/ExportedProject/Assets/Scripts/Assembly-CSharp/ItemID.cs"
        runtimeConfigType = "c_item"
        runtimeConfigAssembly = "BepInEx/interop/Assembly-CSharp-firstpass.dll"
        extensionType = "c_itemExtension"
    }
    counts = [ordered]@{
        itemIdConstants = $entries.Count
        uniqueItemIds = ($entries.id | Sort-Object -Unique).Count
        duplicateIdGroups = $duplicateGroups.Count
    }
    duplicateIdGroups = $duplicateGroups
    entries = $entries
    runtimeConfigFields = $runtimeFields
    extensionMethods = $extensionMethods
}

$jsonDirectory = Split-Path -Parent $JsonOutput
$markdownDirectory = Split-Path -Parent $MarkdownOutput
New-Item -ItemType Directory -Path $jsonDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $markdownDirectory -Force | Out-Null

$catalog | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $JsonOutput -Encoding UTF8

$lines = [System.Collections.Generic.List[string]]::new()
$mdTick = [char]96
$lines.Add("# 物品目录（静态 ID 版）")
$lines.Add("")
$lines.Add("> 生成自 AssetRipper 导出的 " + $mdTick + "ItemID.cs" + $mdTick + "。本目录准确记录静态名称和整数 ID，但不把名称推断为实际效果。")
$lines.Add(">")
$lines.Add("> 运行时配置字段来自 " + $mdTick + "Assembly-CSharp-firstpass.dll" + $mdTick + " 的 " + $mdTick + "c_item" + $mdTick + "；字段值、中文文本和效果参数需要在游戏运行时读取。")
$lines.Add("")
$lines.Add("## 摘要")
$lines.Add("")
$lines.Add("| 项目 | 数量/值 |")
$lines.Add("|---|---:|")
$lines.Add("| ItemID 常量 | $($entries.Count) |")
$lines.Add("| 唯一整数 ID | $(($entries.id | Sort-Object -Unique).Count) |")
$lines.Add("| 重复 ID 组 | $($duplicateGroups.Count) |")
$lines.Add("| 配置类型 | " + $mdTick + "c_item" + $mdTick + " |")
$lines.Add("| 配置程序集 | " + $mdTick + "Assembly-CSharp-firstpass.dll" + $mdTick + " |")
$lines.Add("")
$lines.Add("## 证据和限制")
$lines.Add("")
$lines.Add("- " + $mdTick + "ItemID.cs" + $mdTick + " 中的 " + $mdTick + "public const int" + $mdTick + " 是当前解包脚本能直接确认的名称/ID 映射。")
$lines.Add("- " + $mdTick + "c_item" + $mdTick + " 是运行时配置模型；互操作程序集能确认字段类型，不能离线提供每条配置实例的值。")
$lines.Add("- " + $mdTick + "c_itemExtension" + $mdTick + " 的 " + $mdTick + "IsFood" + $mdTick + "、" + $mdTick + "IsMedicine" + $mdTick + "、" + $mdTick + "GetName" + $mdTick + " 等方法是运行时分类/本地化入口，不能用空的 AssetRipper 方法体推导结果。")
$lines.Add("- 同一个整数可能有多个语义别名，Mod 日志应保留整数 ID，并允许显示多个别名。")
$lines.Add("")
$lines.Add("## 重复 ID")
$lines.Add("")
if ($duplicateGroups.Count -eq 0) {
    $lines.Add("未发现重复 ID。")
}
else {
    $lines.Add("| ID | 别名 |")
    $lines.Add("|---:|---|")
    foreach ($group in $duplicateGroups) {
        $lines.Add("| $($group.id) | $($group.aliases -join ', ') |")
    }
}
$lines.Add("")
$lines.Add("## ItemID 完整列表")
$lines.Add("")
$lines.Add("| 序号 | 名称 | ID |")
$lines.Add("|---:|---|---:|")
for ($index = 0; $index -lt $entries.Count; $index++) {
    $entry = $entries[$index]
    $lines.Add("| $($index + 1) | $mdTick$($entry.name)$mdTick | $($entry.id) |")
}
$lines.Add("")
$lines.Add("## c_item 运行时字段")
$lines.Add("")
$lines.Add("| 字段 | 类型 | 分组 |")
$lines.Add("|---|---|---|")
foreach ($field in $runtimeFields) {
    $lines.Add("| $mdTick$($field.name)$mdTick | $mdTick$($field.type)$mdTick | $($field.group) |")
}
$lines.Add("")
$lines.Add("## c_itemExtension 分类/读取入口")
$lines.Add("")
foreach ($method in $extensionMethods) {
    $lines.Add("- $mdTick$method$mdTick")
}
$lines.Add("")
$lines.Add("## 下一步运行时读取")
$lines.Add("")
$lines.Add("目录的下一阶段不是猜测数值，而是在 " + $mdTick + "GameplayReady" + $mdTick + " 后只读访问游戏的配置容器：")
$lines.Add("")
$lines.Add("1. 找到 " + $mdTick + "ItemManage" + $mdTick + "/" + $mdTick + "ConfigData" + $mdTick + " 中的 " + $mdTick + "c_item" + $mdTick + " 列表或字典。")
$lines.Add("2. 为每条配置读取 " + $mdTick + "item_id" + $mdTick + "、" + $mdTick + "L_item_name" + $mdTick + "、" + $mdTick + "item_type" + $mdTick + "、堆叠/使用/耐久/生产字段。")
$lines.Add("3. 通过 " + $mdTick + "c_itemExtension.GetName" + $mdTick + "、" + $mdTick + "IsFood" + $mdTick + "、" + $mdTick + "IsMedicine" + $mdTick + " 等入口补充本地化和分类。")
$lines.Add("4. 将运行时快照写入单独的 " + $mdTick + "item-runtime-catalog.json" + $mdTick + "，与本静态目录合并时按整数 ID 关联。")
$lines.Add("5. 用止痛片、安眠药、啤酒、苹果和一件装备做最小回归，验证使用、属性变化和装备移动。")

$lines | Set-Content -LiteralPath $MarkdownOutput -Encoding UTF8
Write-Host "Generated $JsonOutput"
Write-Host "Generated $MarkdownOutput"
