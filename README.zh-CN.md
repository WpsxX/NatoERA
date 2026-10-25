<div align="center">

**[简体中文](README.zh-CN.md)** &nbsp;|&nbsp; **[English](README.md)**

</div>

---

# NATO ERA v1.2.6 — 中文

> Fork 自 [Cyances/NATO-ERA](https://github.com/Cyances/NATO-ERA)。适配 GHPC `20261014` 版本。

特别感谢 thebeninator 在本 mod 的编程和 3D 建模方面提供的建议。

## 功能

- 为 M1/M1IP 艾布拉姆斯和 M2 布雷德利添加可配置的爆炸反应装甲
- 可配置 ERA 的布置方式与防护性能
- 可配置 ERA 颜色（方便你搭配自己的涂装）
- 可选的 ERA 重量惩罚
- 兼容原版艾布拉姆斯、M1A1 Abrams v1.1.4+、M1A1 Abrams AMP v2.3.3+、50mm Bradley、M3A2 Bradley 和 M6A2 ADATS 等 mod
- 只建模了 ARAT-1、BRAT M3 和 BRAT M5 三种砖块，没有建模自定义裙板等其它部件，以尽量减少性能开销
- 未建模 M4 砖块等三角形部件，因为三角形不是 Unity 编辑器的原生图元

## 注意

- 如果你想给 ERA 应用自己的贴图，可以向我索取所需的模型文件
- ARAT-1 单块砖重 29.48 KG，总重量四舍五入到个位
- 找不到 BRAT 的官方重量，因此假定其与 ARAT 相同

![ARAT-1 Standard Array](docs/Images/Standard%20ARAT-1.png)
![BRAT Standard](docs/Images/Standard%20BRAT.png)
![Flork Spec](docs/Images/Flork%20Spec.png)
![Indigo ARAT](docs/Images/Indigo%20ARAT-1.png)

## 安装

1. 安装 [MelonLoader](https://github.com/LavaGang/MelonLoader/)。

2. 从 [release 页面](https://github.com/WpsxX/NatoERA/releases) 下载 NATO ERA v#.#.# 的 .tar 或 .zip。

3. 解压文件并按下面的方式放置：

![Mods Folder](docs/Images/Mods%20Folder.PNG)
![NatoAssetsEra Folder](docs/Images/NatoEraAssets%20Folder%20v2.PNG)

4. 启动游戏。

5. 首次运行本 mod 时，`MelonPreferences.cfg` 中的条目需要先启动游戏再关闭后才会出现。

## ERA 防护性能

| 类型 | 抗动能弹（KE）防护（mm） | 抗化学能弹（CE）防护（mm） | 备注 |
| ------------- | ------------- | ------------- | ------------- |
| ARAT-1(M) | 30 | 450 | M 版为半尺寸 |
| 改进型 ARAT-1(M) | 150 | 600 | 可单独开关 |
| BRAT-M3(M) | 30 | 450 | 车体前部与附加阵列 |
| BRAT-M5 | 45 | 600 | 车体侧面 |
| 改进型 BRAT-M3(M) | 150 | 600 | 可单独开关 |
| 改进型 BRAT-M5 | 200 | 800 | 可单独开关 |

## Mod 配置（位于 UserData/MelonPreferences.cfg）

| 选项 | 默认值 | 增加重量（KG） | 备注 |
| ------------- | ------------- | ------------- | ------------- |
| M1 ERA | true | 1,887 | 车体侧面阵列。同时也是总开关，控制是否显示 ERA，优先于下方所有选项。 |
| M1 Hull Lower Front ERA | false | 295 |  |
| M1 Hull Upper Front ERA | false | 1,150 |  |
| M1 Extended Hull ERA | false | 769 |  |
| M1 Turret ERA | false | 678 | 炮塔侧面阵列。同时也是总开关，控制是否显示其它炮塔 ERA 阵列，优先于下方所有选项。 |
| M1 Turret Roof ERA | false | 501 |  |
| M1 Turret Cheek ERA | false | 383 |  |
| M1 Extended Turret ERA | false | 413 |  |
| - | - | - | - |
| M1IP ERA | true | 1,887 | 车体侧面阵列。同时也是总开关，控制是否显示 ERA，优先于下方所有选项。 |
| M1IP Hull Lower Front ERA | false | 295 |  |
| M1IP Hull Upper Front ERA | false | 1,150 |  |
| M1IP Extended Hull ERA | false | 769 |  |
| M1IP Turret ERA | false | 737 | 炮塔侧面阵列。同时也是总开关，控制是否显示其它炮塔 ERA 阵列，优先于下方所有选项。 |
| M1IP Turret Roof ERA | false | 501 |  |
| M1IP Turret Cheek ERA | false | 383 |  |
| M1IP Extended Turret ERA | false | 413 |  |
| - | - | - | - |
| Feather ARAT | true |  | 移除重量惩罚 |
| - | - | - | - |
| Improve CE Protection (600mm) | false |  |  |
| Improve KE Protection (150mm) | false |  |  |
| ARAT R / ARAT G / ARAT B | 71, 80, 65 |  | RGB 数值，默认为军绿色。 |
| - | - | - | - |
| M2 ERA | true | 2,211 | 车体侧面与正面阵列。同时也是总开关，控制是否显示 ERA，优先于下方所有选项。 |
| M2 Extended Hull Side ERA | false | 531 |  |
| M2 Hull Side Lower ERA | true | 825 |  |
| M2 Extended Hull Side Lower ERA | false | 118 |  |
| M2 Hull Lower Front ERA | false | 251 |  |
| M2 Hull Upper ERA | false | 671 |  |
| - | - | - | - |
| Feather BRAT | true |  | 移除重量惩罚 |
| - | - | - | - |
| Improve BRAT-M3 CE Protection (600mm) | false |  |  |
| Improve BRAT-M3 KE Protection (150mm) | false |  |  |
| Improve BRAT-M5 CE Protection (800mm) | false |  |  |
| Improve BRAT-M5 KE Protection (200mm) | false |  |  |
| BRAT R / BRAT G / BRAT B | 71, 80, 65 |  | RGB 数值，默认为军绿色。 |

本 fork 在原版 34 项之外新增了以下 3 项，用来解决新版游戏和 M2Extended 兼容问题。它们排在原版条目之后，不影响原版条目顺序，默认值等于原版行为：

| 选项 | 默认值 | 增加重量（KG） | 备注 |
| ------------- | ------------- | ------------- | ------------- |
| NatoEra Caliber Threshold (mm) | 25 |  | 口径达到或超过此值的弹丸，即使没能击穿也会把爆反砖块引爆，因此大口径炮弹被挡下时依然会点着爆反。口径低于此值的弹丸则要靠穿深说话。 |
| NatoEra Penetration Threshold (mm) | 40 |  | 口径低于口径阈值的弹丸被砖块挡下后，穿深超过此值同样会引爆；两个阈值都不满足时砖块只吸收命中而不引爆。 |
| NatoEra M2Extended Overlay | true |  | 装载 M2Extended mod 的附加装甲时，把 BRAT 向外移，使其位于附加装甲外侧。未装 M2Extended 时不触发。 |

载具的 ERA 布置可以在重新加载任务时更改，无需重启游戏。只需 Alt+Tab 切出修改 .cfg 文件，保存后重开任务或开始新任务即可。但若要看到更新后的 ARAT 防护性能或颜色，则需要重启游戏。

![Configuration File](docs/Images/NATO%20ERA%20MelonPreferences%20v2.PNG)
