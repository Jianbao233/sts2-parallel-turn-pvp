# PVP 选卡系统（方案 C）设计草案（待商榷）

> 状态：草案存档
>
> 说明：本文件用于记录“独立 PVP 商店式选卡系统（方案 C）”的阶段性设计内容，**仅供讨论与迭代**，不作为定版规范。

## 1. 设计目标

### 1.1 核心目标
1. 在 PVP 中提供“商店买卡 + 刷新”体验。
2. 提升卡组可构筑性，减少“全是废牌、无法成型”的对局。
3. 支持玩家主动构筑方向（大类/流派/功能位）。
4. 保持对战公平（避免“谁脸好谁赢”）。

### 1.2 非目标
- 不依赖原版 `MerchantRoom` 的完整生命周期。
- 不追求完全复刻单机商店概率。
- 首版不做双人共享市场（先做“各自商店 + 同规则”）。

## 2. 设计原则（围绕可构筑性）

1. **构筑优先于纯随机**：每轮至少给到“可用牌”与“方向牌”。
2. **允许转型（Pivot）**：中期可切构筑，不会锁死早期选择。
3. **功能位保障**：过牌/能量/防御等基础模块长期可见。
4. **可解释刷新**：刷新按钮对应明确意图（大类、功能、流派）。
5. **可调参数化**：规则尽量由配置驱动，便于赛季平衡。
6. **模式与策略解耦**：流程引擎稳定、模式规则可插拔、选卡策略可替换，避免大量 `if(mode)` 分支。

## 3. 系统总览（多模式可扩展）

### 3.1 流程（Engine 统一）
- 回合开始进入 `PvpCardShopScreen`
- 引擎按 `modeId` 读取模式定义与策略包
- 生成 N 张可购买卡牌（默认 8）
- 玩家可：
  - 购买卡牌
  - 使用刷新按钮重抽部分或全部卡牌
  - 锁定 1 张（可选机制）
- 结束选卡后进入战斗阶段

### 3.2 分层结构
- `PvpShopEngine`：稳定流程内核（开店、刷新、购买、结算、同步编排）
- `PvpModeRegistry`：模式注册与加载（`modeId -> modeDefinition`）
- `PvpStrategyRegistry`：策略包注册与加载（`strategyPackId -> strategyPack`）
- `IShopStrategyPack`：选卡策略组合（分析、评分、约束、刷新、经济）
- `PvpSyncService`：主机权威与事件广播
- `PvpTelemetryService`：可观测日志与回放审计

### 3.3 模式与策略关系
- 一个模式绑定一个默认策略包，但支持热切换版本（例如 `ShopDraft.Standard.v1` 升到 `v2`）。
- 多模式可复用同一策略包（只改模式配置，不改算法代码）。
- 策略包可被多个模式继承并覆写少量参数（例如刷新费用、槽位模板、保底阈值）。

## 4. 数据模型（关键）

### 4.1 卡牌元信息（建议扩展）
每张卡除原属性外，增加构筑标签：
- `PrimaryClass`: Attack / Skill / Power
- `RoleTags`: Draw / Energy / Block / Scaling / Finisher / Utility
- `ArchetypeTags`: 如 Burn / Shiv / Poison / Frost / Combo …
- `CurveCost`: 费用档位（0/1/2/3+）
- `BuildScoreBase`: 基础构筑价值（平衡可调）

### 4.2 玩家构筑画像 `DeckProfile`
- `classRatio`: 攻/技/能力比例
- `roleCoverage`: 功能位覆盖度
- `archetypeVector`: 各流派权重
- `curveStats`: 费用曲线缺口
- `stabilityScore`: 稳定性评分（过牌+能量+防御）
- `pivotWindow`: 是否需要给转型牌

### 4.3 商店状态 `ShopState`
- 当前卡槽列表
- 已刷新次数
- 本轮锁定卡
- 保底计数器（核心牌保底、功能牌保底）
- 历史已看卡（降重复）

### 4.4 运行上下文（新增）
- `ModeContext`: `modeId`、`strategyPackId`、赛季版本、禁卡池版本
- `OfferContext`: `DeckProfile`、回合信息、对局差值、随机种子分段
- `StrategyRuntimeState`: 策略临时状态（最近命中约束、降重复窗口、保底进度）

## 5. 可构筑性强化机制（重点）

> 以下为默认策略包 `ShopDraft.Standard.v1` 的建议机制，可被其他模式覆写。

### 5.1 槽位模板（每次出卡时固定结构）
建议 8 槽：
1. 核心方向槽 x2（匹配主流派）
2. 功能修复槽 x2（过牌/能量/防御）
3. 大类定向槽 x2（跟随玩家偏好 Attack/Skill/Power）
4. 转型槽 x1（次流派/中立强牌）
5. 高天花板槽 x1（稀有成长件）

### 5.2 核心件保底
- 若连续 2 轮未出现主流派关键标签，则第 3 轮强制插入 1 张。
- 若稳定性评分过低（例如缺能量），功能槽优先补能量牌。

### 5.3 功能位保障
每轮至少出现：
- `Draw/Energy/Block` 三者之一的 2 张以上。

### 5.4 反重复机制
- 本轮不重复同卡 ID
- 最近 2 轮已出现卡权重衰减
- 已购买同类过多时，自动提升互补牌权重

### 5.5 可转型窗口
- 中期（例如第 4~6 轮）提高次流派牌曝光率
- 给 1 个“桥接牌位”（既可补当前构筑，也能转型）

## 6. 出卡算法（管线化，可替换）

### 6.1 评分函数（默认项）
对候选卡 `c` 计算：

```text
Score(c) =
  BaseWeight(c)
* ArchetypeFit(c, profile.primaryArchetype)
* RoleNeed(c, profile.roleCoverageGap)
* CurveFit(c, profile.curveGap)
* ClassIntent(c, playerSelectedClassBias)
* Novelty(c, recentSeenHistory)
* BalanceFactor(c, matchPowerDelta)
```

> 评分项拆为 `IScoringTerm`，支持按模式增删（例如某模式去掉 `BalanceFactor`，另一模式新增 `TempoPressure`）。

### 6.2 生成步骤（Offer Pipeline）
1. 读取 `DeckProfile`
2. `CandidateSource` 按槽位构建候选池
3. `ScoringTerms` 计算分值并归一化
4. `Sampler` 进行加权抽样
5. `Constraints` 应用约束（去重、保底、禁卡、功能位保障）
6. 产出 `OfferBundle` 并写入 `ShopState`

### 6.3 与刷新联动（策略化）
- 普通刷新：全槽重算
- 定向刷新：仅重算指定槽（例如“大类定向槽”）
- 不同模式可通过 `IRefreshPolicy` 定义刷新类型、影响槽位、触发条件

## 7. 刷新按钮设计（商店体验核心）

建议首版 4 个按钮：

1. **普通刷新**
   - 重刷全部 8 张
   - 消耗：低（例如 20 金）

2. **大类刷新（攻/技/能力三选一）**
   - 仅重刷大类定向槽 + 1 个核心槽
   - 消耗：中（例如 30 金）

3. **构筑修复刷新**
   - 仅重刷功能槽（优先出 Draw/Energy/Block）
   - 消耗：中（例如 30 金）

4. **流派追踪刷新**
   - 强化主流派标签候选
   - 消耗：高（例如 45 金，且每轮限 1 次）

刷新费用递增公式建议：

\[
cost_n = base \times (1 + 0.35n)
\]

## 8. 经济与平衡

### 8.1 货币
- 每轮固定收入 + 战斗表现奖励
- 刷新与买卡共用同一货币

### 8.2 防滚雪球
- 领先方高价值卡权重轻微下调
- 落后方“功能修复槽”权重上调
- 只做轻度橡皮筋

### 8.3 稀有度控制
- 稀有卡出现概率随轮次小幅提升
- 首版不放开过高稀有度

## 9. 联机同步与公平

### 9.1 权威模型
- 主机权威生成卡组市场（或验证客户端请求）
- 客户端只发送“按钮点击/购买请求”
- 主机返回结果与状态快照

### 9.2 同步最小事件
- `ShopOpened(modeId, strategyPackId, stateVersion)`
- `RefreshRequested(type, args)`
- `OfferUpdated(slots, costs, seedSegment, stateVersion)`
- `CardPurchased(cardId, slotIndex, cost, stateVersion)`
- `ShopClosed`

### 9.3 防作弊
- 刷新结果不由客户端本地决定
- 每次变更写日志（玩家、回合、按钮、消耗、结果）
- 回放可用：日志里带 `modeVersion/strategyVersion/rngVersion`

## 10. UI/UX 设计建议

1. 左侧显示“构筑雷达”
2. 刷新按钮下方显示“本次会影响哪些槽位”
3. 每张卡显示“适配提示”
4. 提供“锁定一张”按钮提升策略深度

## 11. 可配置化（保证延展性）

### 11.1 配置分层
- **EngineConfig**：协议版本、默认槽位上限、通用硬约束
- **ModeConfig**：模板、按钮、经济、可用池、禁卡池
- **StrategyConfig**：评分项权重、约束参数、保底规则
- **SeasonConfig**：赛季启用模式、版本钉住、热修参数

### 11.2 版本化要求
建议所有配置携带：
- `schemaVersion`
- `modeVersion`
- `strategyVersion`
- `rngVersion`

### 11.3 新模式接入最小成本
新增模式时，优先按顺序尝试：
1. 只加 `ModeConfig`
2. 不够再复用策略包并覆写参数
3. 仍不够再新增策略包实现

## 12. 可构筑性 KPI（上线后观测）

1. `Dead Offer Rate`（无效展示率） < 12%
2. 第 4 轮功能位达标率 > 75%
3. 主流派连续可选率 > 70%
4. 中期转型成功率 > 35%
5. 构筑多样性指数不过度集中

## 13. 工程补充：配置 + 接口草案（待讨论）

### 13.1 配置示意（多模式）

```json
{
  "schemaVersion": 1,
  "engine": {
    "rngVersion": "v1",
    "maxSlots": 12
  },
  "modes": [
    {
      "modeId": "shop_draft_standard",
      "modeVersion": "1.0.0",
      "strategyPackId": "shopdraft.standard.v1",
      "shop": {
        "slots": 8,
        "template": ["CoreArchetype", "CoreArchetype", "RoleFix", "RoleFix", "ClassBias", "ClassBias", "Pivot", "HighCeiling"]
      },
      "refresh": {
        "baseCost": {"Normal": 20, "ClassBias": 30, "RoleFix": 30, "ArchetypeTrace": 45},
        "costGrowth": 0.35
      }
    }
  ],
  "strategyPacks": [
    {
      "id": "shopdraft.standard.v1",
      "strategyVersion": "1.0.0",
      "scoringWeights": {
        "ArchetypeFit": 1.0,
        "RoleNeed": 1.0,
        "CurveFit": 1.0,
        "ClassIntent": 0.9,
        "Novelty": 0.8,
        "BalanceFactor": 0.6
      }
    }
  ]
}
```

### 13.2 模块接口草案（可插拔）

```csharp
public interface IPvpShopEngine
{
    Task OpenAsync(PlayerContext player, RoundContext round, string modeId);
    Task CloseAsync(PlayerContext player);
    Task<ShopViewModel> GetCurrentViewAsync(PlayerContext player);
    Task<PurchaseResult> PurchaseAsync(PlayerContext player, int slotIndex);
    Task<RefreshResult> RefreshAsync(PlayerContext player, RefreshType type, RefreshArgs? args = null);
}

public interface IPvpModeDefinition
{
    string ModeId { get; }
    string ModeVersion { get; }
    string StrategyPackId { get; }
    ShopTemplateConfig ShopTemplate { get; }
    RefreshConfig Refresh { get; }
    EconomyConfig Economy { get; }
    BanPoolConfig BanPool { get; }
}

public interface IShopStrategyPack
{
    string Id { get; }
    string Version { get; }
    IDeckAnalyzer DeckAnalyzer { get; }
    IOfferPipeline OfferPipeline { get; }
    IRefreshPolicy RefreshPolicy { get; }
    IEconomyPolicy EconomyPolicy { get; }
}

public interface IOfferPipeline
{
    OfferBundle Generate(OfferContext context, ShopState state);
}

public interface IScoringTerm
{
    string Name { get; }
    float Eval(CardModel card, OfferContext context);
}

public interface IOfferConstraint
{
    void Apply(OfferBundle offer, OfferContext context, ShopState state);
}

public interface IDeckAnalyzer
{
    DeckProfile Analyze(IEnumerable<CardModel> deck, BuildMeta meta);
}
```

## 14. 实施路线（建议）

1. 先把当前方案 C 固化为 `shopdraft.standard.v1`。
2. 把现有硬编码参数全部迁到 `ModeConfig + StrategyConfig`。
3. 引入 `PvpShopEngine` 与注册中心，但逻辑保持等价，先不改玩法。
4. 新增第二模式（例如“快节奏短局”）验证扩展成本。
5. 通过模拟器跑固定 seed 回归，再开放到联机实测。

## 15. 状态声明

- 本文件为 **讨论稿/存档稿**。
- 当前内容用于后续评审、参数推演与原型验证。
- 任何数值、流程、接口均可能调整，不视为最终实现承诺。