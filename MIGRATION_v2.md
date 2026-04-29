# Engine 2.0 迁移 — Windows 端待验证清单

工作机是 macOS，跑得起 dotnet build 但跑不动 ACT 本体。这份清单是落到 Windows + 真实 ACT 环境继续验证的事项。

## Engine 2.0 改了什么（影响 ACT 端的部分）

`MemoEngine` 子模块从 1.0.7 升到 2.0.0（`3c50b05` → `1db2e5f`）。

- **DSL 重写**：`timeline / checkpoints / transitions / subphase` 全删；phase 用 `predicate`（TARGETABLE / SEEN / VAR / STATUS_ON / HP_RATIO + AND/OR/NOT）描述。Active phase = 声明顺序里最后一个谓词成立的。
- **Trigger 多态**：`ACTION_START / ACTION_COMPLETE / STATUS_APPLIED / STATUS_REMOVED`，`*_ids` 是数组（"any of"），LOGICAL_OPERATOR 删除。
- **Action 重命名**：`INCREMENT_VARIABLE / SET_VARIABLE` → `INCREMENT / SET`，字段 `name` → `variable`。
- **Payload 改版**：`FightProgressPayload` 删 `subphase` 加 `phase_name`。服务端 schema 同步迁移中。
- **行为修复**：`e.TimeStamp` 替换 `DateTime.UtcNow`；TerritoryChanged 同步清状态再 fetch；INCREMENT 内部用 long；FindIndex 异常包 try/catch；EventRecorder 改惰性快照；ActionBlock 加 `BoundedCapacity = 4096`；删除死路径 `PartyChanged`。

## ACT 端代码变化

- **零行为代码改动**——`Events/EventManager.cs` 的解析逻辑、Sink 调用都没动。Sink 接口 (`Event.General/Action/Combatant/Status.Raise*`) 在 2.0 里签名完全一致。
- **csproj `<Compile Include>` 只是文件清单更新**：去掉 `Tools.cs / FightContext.cs / ListenerManager.cs`，加上 `PredicateEngine.cs / WorldModel.cs / Predicate.cs / Trigger.cs / MechanicAction.cs`。
- **AssemblyVersion** 7.4.7.2 → 7.5.0.0。
- 加了 `Microsoft.NETFramework.ReferenceAssemblies.net48` PackageReference，纯粹方便 macOS/Linux 跑 dotnet build；Windows + Framework SDK 环境无副作用。

## Windows 上的验证清单

打勾后可以删掉这个文件。

### 编译 / 打包
- [ ] `nuget restore Plugin.sln && msbuild Plugin.sln /p:Configuration=Release` 成功（GitHub Actions release.yaml 应该会代跑）
- [ ] `MemoUploader.dll` + 依赖 dll 一起打 zip 上传到 release

### 运行时基本性
- [ ] 在 ACT 里加载插件，"酥卷 SuMemo" tab 出现
- [ ] InitPlugin 不抛异常（看 ACT 日志）
- [ ] `runtime.log` 写得动
- [ ] UpdateHelper 抓 manifest.json 不报错

### 事件流端到端（建议先用一个老本子，比如 m11s 或 m9s 这种已迁好的）
- [ ] 进副本：[Lifecycle] log 显示 territory change，引擎完成 duty fetch，Lifecycle 进入 `WaitingStart`
- [ ] 开战：CombatOptIn 事件触发，Lifecycle 切到 `Recording`，`Context.EnemyDataId` 应填上首个 phase 的 TARGETABLE 值
- [ ] 触发 phase 推进的 mechanic（比如 m11s 的狂暴 cast 42141）：active phase 应当推进到 "狂暴" 阶段（PhaseName = 狂暴）
- [ ] 团灭/通关：`OnFightFinalized` 触发，`ApiClient.UploadFight` 投递 — **现在** 服务端是 v1 schema，新 payload 没有 subphase，**会被 server 拒**。这一步等 server 也升 v2 后再验。
- [ ] 验证 boss 切换场景（m12s 的 门神 → 本体）`Context.EnemyDataId` 跟着切

### 服务端联调（依赖 server v2 上线）
- [ ] payload 里 `progress.phase_name` 和 `progress.enemy_hp` 字段被服务端正确接受
- [ ] 不再发送 `progress.subphase` 字段
- [ ] X-Client-Name / X-Client-Version header 正确（meta.sources 改造之后看是否被 server 记录）

### 老 yaml 兼容性
- [ ] 进 m9s/m10s/m11s/m12s/top/uwu/e1n 任意一个，引擎都能成功 fetch 新 yaml 并构造 PredicateEngine（不抛 JsonSerializationException）

## 已知留的坑

1. **STATUS_APPLIED.target_data_id 字段未实现**：YAML schema 里支持，但 PredicateEngine 当前忽略（因为 entityId → dataId 映射没建）。当前 yaml 没用到这个 filter，将来如需要再补。
2. **m7s/m8s yaml 已删除**（用户决定不再维护）。
3. **新 engine 包发布**：`MemoEngine 2.0.0` 已上 NuGet.org（5/29 推送，2 分钟内可用）。
