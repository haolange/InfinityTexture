# InfinityVirtualTexture
Runtime Procedural Virtual Texture for Unity Terrain in Universal Render Pipeline(URP).

![image](https://user-images.githubusercontent.com/12471727/170739477-57cf2c46-94d5-4372-a751-c435f6439f86.png)

![image](https://user-images.githubusercontent.com/12471727/170739453-89b2d440-26fa-484a-9f0a-368b42729a14.png)

## 实时升级任务（2026-10-22）

基线：`main@0e4ec82`，Unity 6000.6.0f1 / URP 17.6 / Apple M3 Max / Metal。其他后端未实测。保持 border 4、单一 RenderGraph 通路；中间纹理数据只在内存中检查。

| 任务 | 状态 | 验证与结论 |
|---|---|---|
| T0 环境与基线 | 已验证 | Metal EditMode 67/0/0；GPU recorder 不支持；单一临时宿主 |
| T1 编码等价优化 | 已验证 | 随机块 17,024 UInt 字与基线一致；完整测试 68/0/0；未宣称 GPU 提速 |
| T2 确定性过滤 | 已验证 | 六组 GPU 对照、三层输出误差 ≤1e-6；完整测试 74/0/0 |
| T3 按层压缩格式 | 已验证 | Linear Metal 80/0/0；五种压缩实际非零槽写入、覆盖与解码通过；BC/ETC2 三层物理图节省 1/6 |
| T4 时间随机 mip | 待处理 | 默认关闭，公开 URP API 与 TAA 验收 |
| T5 硬件各向异性 | 待处理 | 梯度、border 与实际收益门槛 |
| T6 页面调度 | 待处理 | 1 / 2 / 4 页；无 GPU 收益证据不采用 |
| T7 完整回归与清理 | 待处理 | 完整测试、文档、提交与临时产物清理 |
