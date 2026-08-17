# 物品目录（静态 ID 版）

> 生成自 AssetRipper 导出的 `ItemID.cs`。本目录准确记录静态名称和整数 ID，但不把名称推断为实际效果。
>
> 运行时配置字段来自 `Assembly-CSharp-firstpass.dll` 的 `c_item`；字段值、中文文本和效果参数需要在游戏运行时读取。

## 摘要

| 项目 | 数量/值 |
|---|---:|
| ItemID 常量 | 284 |
| 唯一整数 ID | 283 |
| 重复 ID 组 | 1 |
| 配置类型 | `c_item` |
| 配置程序集 | `Assembly-CSharp-firstpass.dll` |

## 证据和限制

- `ItemID.cs` 中的 `public const int` 是当前解包脚本能直接确认的名称/ID 映射。
- `c_item` 是运行时配置模型；互操作程序集能确认字段类型，不能离线提供每条配置实例的值。
- `c_itemExtension` 的 `IsFood`、`IsMedicine`、`GetName` 等方法是运行时分类/本地化入口，不能用空的 AssetRipper 方法体推导结果。
- 同一个整数可能有多个语义别名，Mod 日志应保留整数 ID，并允许显示多个别名。

## 重复 ID

| ID | 别名 |
|---:|---|
| 360 | Galoshes, RubberShoes |

## ItemID 完整列表

| 序号 | 名称 | ID |
|---:|---|---:|
| 1 | `Energy` | 5 |
| 2 | `Health` | 6 |
| 3 | `Mentality` | 7 |
| 4 | `Satiety` | 8 |
| 5 | `Discipline` | 9 |
| 6 | `Prestige` | 10 |
| 7 | `Money` | 11 |
| 8 | `Friend` | 12 |
| 9 | `GangFriend` | 13 |
| 10 | `BasketballShoes` | 14 |
| 11 | `Slipper` | 17 |
| 12 | `Nightcap` | 22 |
| 13 | `RedHeadband` | 23 |
| 14 | `RubberGloves` | 25 |
| 15 | `HeiShou` | 26 |
| 16 | `HuPo_HuShenFu` | 29 |
| 17 | `Mask` | 32 |
| 18 | `GuardGasMask` | 33 |
| 19 | `PersonalStereoOn` | 34 |
| 20 | `PersonalStereoOff` | 35 |
| 21 | `PersonalStereoNoPower` | 36 |
| 22 | `PokerA` | 39 |
| 23 | `Catnip` | 40 |
| 24 | `MintRoll` | 41 |
| 25 | `Mushroom` | 42 |
| 26 | `MushroomPowder` | 43 |
| 27 | `Laxative` | 44 |
| 28 | `ForgetWorryFlower` | 45 |
| 29 | `SleepingPill` | 47 |
| 30 | `SleepingCookies` | 48 |
| 31 | `Painkiller` | 49 |
| 32 | `AlcoholLamp` | 52 |
| 33 | `Tranquilizer` | 53 |
| 34 | `Beer` | 54 |
| 35 | `CraftSpirits` | 56 |
| 36 | `CoffeeBean` | 69 |
| 37 | `Apple` | 64 |
| 38 | `ChewingGum` | 70 |
| 39 | `Sodas` | 73 |
| 40 | `PotatoPizza` | 75 |
| 41 | `CoffeePowder` | 76 |
| 42 | `TeaBag` | 77 |
| 43 | `SpicyNoodles` | 78 |
| 44 | `AlbumenPowder` | 79 |
| 45 | `ZhenTou` | 80 |
| 46 | `TanZi` | 81 |
| 47 | `JiaRen` | 82 |
| 48 | `ChuangeDan` | 83 |
| 49 | `Rope` | 85 |
| 50 | `PaperClip` | 86 |
| 51 | `Unlocker` | 87 |
| 52 | `Soap` | 88 |
| 53 | `ScentedSoap` | 89 |
| 54 | `PrisonGuard8KeyMould` | 90 |
| 55 | `UnlockerP` | 38 |
| 56 | `Corrosive` | 96 |
| 57 | `Calculator` | 101 |
| 58 | `MagazineNew` | 102 |
| 59 | `MagazineSeen` | 103 |
| 60 | `MagazineRotten` | 104 |
| 61 | `coffeeGrinder` | 109 |
| 62 | `GameConsole` | 110 |
| 63 | `GameConsoleNoPower` | 111 |
| 64 | `flashlight` | 112 |
| 65 | `flashlightNoPwer` | 113 |
| 66 | `Battery` | 114 |
| 67 | `ToothBrush` | 115 |
| 68 | `Toothpaste` | 116 |
| 69 | `RustRemover` | 119 |
| 70 | `Match` | 120 |
| 71 | `Tape` | 121 |
| 72 | `Pigment` | 122 |
| 73 | `Nail` | 123 |
| 74 | `Paper` | 125 |
| 75 | `Zh` | 126 |
| 76 | `Pencil` | 131 |
| 77 | `BallPointPen` | 133 |
| 78 | `BoltDriver` | 138 |
| 79 | `Hammer` | 151 |
| 80 | `Scissors` | 152 |
| 81 | `ShatteredGlass` | 153 |
| 82 | `GlassDagger` | 154 |
| 83 | `YsShuaZhuiCi` | 156 |
| 84 | `FruitKnife` | 158 |
| 85 | `Baton` | 165 |
| 86 | `Belt` | 166 |
| 87 | `PrisonGuardUniform` | 189 |
| 88 | `PoliceWhistle` | 190 |
| 89 | `HeavyKey` | 193 |
| 90 | `LetterForDoctor` | 194 |
| 91 | `LetterForGirlFriend` | 195 |
| 92 | `ArticlePraisingPrison` | 196 |
| 93 | `LetterForFiction` | 197 |
| 94 | `TuShouKaiSuo` | 201 |
| 95 | `Scarab` | 202 |
| 96 | `SZZ` | 203 |
| 97 | `SenZhiYinProcessed` | 204 |
| 98 | `MysteryPaperBag` | 205 |
| 99 | `BeiYongZhangBu` | 210 |
| 100 | `ZhiHu` | 212 |
| 101 | `JingZhiYeJuan` | 214 |
| 102 | `Sandals` | 218 |
| 103 | `PunkSunglasses` | 219 |
| 104 | `ElectronicWatch` | 229 |
| 105 | `ShiKe_HuShenFu` | 231 |
| 106 | `MuDiao_HuShenFu` | 232 |
| 107 | `JiuHuLu` | 37 |
| 108 | `JiuHuLu_Shui` | 236 |
| 109 | `JiuHuLu_Jiu` | 237 |
| 110 | `FakeLedger` | 238 |
| 111 | `BarCard` | 239 |
| 112 | `PriorityVisitingRight` | 240 |
| 113 | `StudentPhoto` | 241 |
| 114 | `StudentPhotoMap` | 242 |
| 115 | `MailRoomPass` | 243 |
| 116 | `LaundryRoomLicence` | 244 |
| 117 | `MailRoomLicence` | 245 |
| 118 | `KitchenLicence` | 296 |
| 119 | `MailRoomBackupPassword` | 247 |
| 120 | `WarehousePwdTrace` | 249 |
| 121 | `DaBaoZhaDCK` | 250 |
| 122 | `LetterFromLeiDe` | 252 |
| 123 | `TransparentSolution` | 253 |
| 124 | `HealthCertificate` | 255 |
| 125 | `RanShaoPing` | 256 |
| 126 | `JingNiangRanShaoPing` | 257 |
| 127 | `HallMap` | 258 |
| 128 | `PlaygroundMap` | 259 |
| 129 | `BoxingBetBill` | 261 |
| 130 | `BallBetBill` | 262 |
| 131 | `RemitMoneyByMaji` | 263 |
| 132 | `PizzaCouponCoupon` | 264 |
| 133 | `LotteryTicket` | 265 |
| 134 | `PrizeCashedTicket` | 267 |
| 135 | `BonusRemittance` | 268 |
| 136 | `HandmadeSoap` | 270 |
| 137 | `BeiGuangWaiShe` | 273 |
| 138 | `Chips` | 274 |
| 139 | `LetterForNike` | 275 |
| 140 | `ColdBeer` | 276 |
| 141 | `RoofLicence` | 277 |
| 142 | `Topic` | 280 |
| 143 | `ChurchVolunteerWorker` | 281 |
| 144 | `RaiseFundsProposal` | 282 |
| 145 | `ContributionsBag` | 283 |
| 146 | `SamplingPalmprintTool` | 284 |
| 147 | `PriestPalmprint` | 285 |
| 148 | `LiKaduoPhoneNumber` | 286 |
| 149 | `SpicyNoodlesBag` | 287 |
| 150 | `Iodine` | 289 |
| 151 | `ZebraDisableDevice` | 291 |
| 152 | `ZebraDisableDeviceinstructions` | 292 |
| 153 | `ZebraMessage` | 293 |
| 154 | `PrisonGuardCaptainMessage` | 294 |
| 155 | `PriestCattleDogCD` | 295 |
| 156 | `DCDKey` | 298 |
| 157 | `Cheese` | 299 |
| 158 | `PickleCucumber` | 300 |
| 159 | `CityMap` | 301 |
| 160 | `SaiLinNaApartkey` | 303 |
| 161 | `TreatmentRoomBackupPassword` | 304 |
| 162 | `DoctorTreatmentPlan` | 305 |
| 163 | `DoctorBirthdayByGift` | 306 |
| 164 | `DoctorBirthdayByConcert` | 307 |
| 165 | `DelegatedPhoto` | 308 |
| 166 | `DreamGirlProfile` | 309 |
| 167 | `ChessGameBlueprint` | 310 |
| 168 | `ChessGameMethodsCracking` | 311 |
| 169 | `TVStationCard` | 312 |
| 170 | `CriminalEvidenceRecording` | 313 |
| 171 | `SpecialPerfume` | 315 |
| 172 | `XiaoKuaiZaoShi` | 316 |
| 173 | `ParoleCertificate` | 317 |
| 174 | `PrisonPaper` | 318 |
| 175 | `MovieCD` | 319 |
| 176 | `TearOffPage` | 320 |
| 177 | `KitchenCellarBlueprint` | 321 |
| 178 | `PinkRouge` | 322 |
| 179 | `ChefKey` | 323 |
| 180 | `PlasticChefKey` | 324 |
| 181 | `PlasticKeyCheap` | 325 |
| 182 | `ChefKeyMould` | 326 |
| 183 | `CheapKeyMould` | 327 |
| 184 | `PaintingsZh` | 334 |
| 185 | `WolfHair` | 335 |
| 186 | `MovieCD336` | 336 |
| 187 | `LuckyCoin` | 337 |
| 188 | `MovieCD338` | 338 |
| 189 | `MovieCD339` | 339 |
| 190 | `MovieCD340` | 340 |
| 191 | `MovieCD341` | 341 |
| 192 | `MovieCD342` | 342 |
| 193 | `MovieCD359` | 359 |
| 194 | `GuanQianTouZi` | 343 |
| 195 | `ShiXiJiZheZheng` | 344 |
| 196 | `Videotape1` | 345 |
| 197 | `Videotape2` | 346 |
| 198 | `Videotape3` | 347 |
| 199 | `WheatBread` | 348 |
| 200 | `AvocadoShake` | 349 |
| 201 | `CheeseConventionPizza` | 350 |
| 202 | `IcedCoffee` | 351 |
| 203 | `SalmonTartar` | 352 |
| 204 | `VegetableSoup` | 353 |
| 205 | `Champagne` | 354 |
| 206 | `GoldenFleeceCigarette` | 355 |
| 207 | `UltimateFightTrophy` | 356 |
| 208 | `GuardLoungeLicence` | 357 |
| 209 | `Galoshes` | 360 |
| 210 | `RubberShoes` | 360 |
| 211 | `QiangXiaoGaiPian` | 361 |
| 212 | `HeighteningInsole` | 362 |
| 213 | `PoliceGloves` | 363 |
| 214 | `BarCardUnknow` | 364 |
| 215 | `Sleeve` | 367 |
| 216 | `Crank` | 368 |
| 217 | `Handle` | 369 |
| 218 | `HomemadeHandle` | 370 |
| 219 | `Pendant` | 371 |
| 220 | `CanvasWithNumberBlood` | 372 |
| 221 | `PasteupBWR` | 373 |
| 222 | `LetterWithPasteup` | 374 |
| 223 | `PhoneNumberBaBaLa` | 375 |
| 224 | `KeyForBillyHide` | 376 |
| 225 | `MoShaFen` | 377 |
| 226 | `BadgeOfDeceased` | 378 |
| 227 | `PortableOxygenTankEmpty` | 379 |
| 228 | `PortableOxygenTank` | 380 |
| 229 | `LittleGoldFish` | 381 |
| 230 | `HospitalTreatmentPermit` | 382 |
| 231 | `Crowbar` | 389 |
| 232 | `SharkPinup` | 390 |
| 233 | `SharPeiBag` | 391 |
| 234 | `CheetahRecordingWatch` | 392 |
| 235 | `FederalLaundryAdCard` | 393 |
| 236 | `CamouflagePrisonMap` | 394 |
| 237 | `MotorcycleGloves` | 396 |
| 238 | `LabradorEntranceCard` | 397 |
| 239 | `ProofOfMentalAssessment` | 399 |
| 240 | `LabradorEntranceCardNumber` | 400 |
| 241 | `ClinicCardPackage` | 401 |
| 242 | `PackageComb` | 402 |
| 243 | `AnestheticNeedle` | 404 |
| 244 | `SpecialTool_Richard` | 405 |
| 245 | `SpecialTool_Screw` | 406 |
| 246 | `SpecialTool_Metalbar` | 407 |
| 247 | `SpecialTool_Crowbar` | 408 |
| 248 | `SpecialTool_Franklin` | 409 |
| 249 | `BatteryP` | 410 |
| 250 | `BatteryN` | 411 |
| 251 | `SpecialTool_Franklin_Finish` | 412 |
| 252 | `FenrirLunch` | 413 |
| 253 | `FenrirDinner` | 414 |
| 254 | `PhysicalDetector` | 415 |
| 255 | `HangGliderCloth` | 416 |
| 256 | `HangGliderkeel` | 417 |
| 257 | `HangGliderTripod` | 418 |
| 258 | `HangGliderHarness` | 419 |
| 259 | `RustyScalpel` | 420 |
| 260 | `Scalpel` | 421 |
| 261 | `JingYuanZheng` | 422 |
| 262 | `Plate` | 423 |
| 263 | `LockedBox1` | 424 |
| 264 | `LockedBox2` | 425 |
| 265 | `VialOfEther` | 426 |
| 266 | `SupporterDLCPackage` | 427 |
| 267 | `SpecialPatternDice` | 428 |
| 268 | `CellDesignCatalogue` | 429 |
| 269 | `OldMusicTape` | 430 |
| 270 | `Post_Panther` | 431 |
| 271 | `Post_Fox` | 432 |
| 272 | `CD_RommEntrance` | 433 |
| 273 | `Note_ThanksEA` | 434 |
| 274 | `PhotoSticker` | 435 |
| 275 | `FourHangingBag` | 436 |
| 276 | `IOUOfKevin` | 438 |
| 277 | `DoctorBirthdayByPassWord` | 440 |
| 278 | `MarmotBadge` | 441 |
| 279 | `MetalHead` | 442 |
| 280 | `ThreePigsPoker` | 443 |
| 281 | `DialogboxSticker` | 444 |
| 282 | `GangContribution` | 1001 |
| 283 | `charm` | 1002 |
| 284 | `focus` | 1003 |

## c_item 运行时字段

| 字段 | 类型 | 分组 |
|---|---|---|
| `item_id` | `int` | identity |
| `item_type` | `string` | classification |
| `item_type_2` | `string` | classification |
| `interactive_type` | `string` | interaction |
| `item_value` | `int` | economy |
| `buy_item_price` | `int` | economy |
| `contraband` | `int` | economy |
| `tradable` | `int` | economy |
| `treasure` | `int` | economy |
| `max_stack` | `int` | stacking |
| `max_use` | `int` | usage |
| `batch_use` | `int` | usage |
| `max_durability` | `int` | durability |
| `fight_durability` | `int` | durability |
| `dice_durability` | `int` | durability |
| `work_durability` | `int` | durability |
| `max_battery` | `int` | battery |
| `battery_p` | `int` | battery |
| `volume` | `int` | inventory |
| `maintenance` | `string` | maintenance |
| `feedable` | `int` | interaction |
| `gift_opinion` | `int` | social |
| `side_effect_p` | `string` | effect |
| `use_limit_p` | `string` | effect |
| `exhaust_get_item` | `string` | effect |
| `produce_material` | `string` | production |
| `produce_number` | `int` | production |
| `produce_order` | `int` | production |
| `produce_time` | `int` | production |
| `produce_type` | `int` | production |
| `produce_energy` | `int` | production |
| `produce_leader` | `string` | production |
| `produce_unlock_1` | `string` | production |
| `item_p_A` | `string` | parameters |
| `item_p_B` | `string` | parameters |
| `item_p_A_weaken` | `string` | parameters |
| `item_p_B_weaken` | `string` | parameters |
| `handbook_type` | `int` | presentation |
| `item_pic_demo` | `string` | presentation |
| `key_to_object` | `string` | presentation |
| `L_item_name` | `string` | localization |
| `L_item_des_A` | `string` | localization |
| `L_item_des_B` | `string` | localization |
| `L_background_des` | `string` | localization |
| `L_side_effect_des` | `string` | localization |
| `L_use_limit_des` | `string` | localization |
| `L_battery_des` | `string` | localization |

## c_itemExtension 分类/读取入口

- `GetName`
- `GetTipsName`
- `GetItemType2`
- `GetTypeDesc`
- `GetDesc`
- `GetBuyPrice`
- `GetBuyRealPrice`
- `GetContraband`
- `GetEquipmentPlaceId`
- `GetCostDurability`
- `GetCostDurabilityDefault`
- `GetCostDurabilityByBattle`
- `GetCostDurabilityByThrowDice`
- `GetItemPARealTime`
- `GetItemPBRealTime`
- `GetOnceProduceNumber`
- `GetProduceMaterial`
- `GetNeedBatteryInfo`
- `GetFinalCostBattery`
- `IsHaveUseLimit`
- `IsHaveAlcohol`
- `IsHaveSideEffect`
- `IsContraband`
- `IsCanBeGift`
- `IsEquipment`
- `IsHaveBackgroundDes`
- `IsHaveDurability`
- `IsHaveBattery`
- `IsHaveUseTimes`
- `IsCanStack`
- `IsTreasure`
- `IsMedicine`
- `IsFood`
- `IsAlcohol`
- `IsFullGrid`
- `IsPackageConsumeItem`
- `IsItemFuncAOpen`
- `IsItemFuncBOpen`
- `IsShowSpecialItemGetTips`
- `IsTaskItem`
- `IsNeedYellowTipsItem`
- `IsHaveItemTitle`
- `IsBillItem`
- `IsKeyItem`
- `IsExtraPocketItem`
- `IsCanBeDestory`

## 下一步运行时读取

目录的下一阶段不是猜测数值，而是在 `GameplayReady` 后只读访问游戏的配置容器：

1. 找到 `ItemManage`/`ConfigData` 中的 `c_item` 列表或字典。
2. 为每条配置读取 `item_id`、`L_item_name`、`item_type`、堆叠/使用/耐久/生产字段。
3. 通过 `c_itemExtension.GetName`、`IsFood`、`IsMedicine` 等入口补充本地化和分类。
4. 将运行时快照写入单独的 `item-runtime-catalog.json`，与本静态目录合并时按整数 ID 关联。
5. 用止痛片、安眠药、啤酒、苹果和一件装备做最小回归，验证使用、属性变化和装备移动。
