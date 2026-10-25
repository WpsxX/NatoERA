# Changelog

## v1.2.6 — 适配 20261014

- ERA 引爆规则改为「被挡下时」判定：口径 ≥ 25mm 直接引爆，口径不足但穿深 > 40mm 也引爆，两者都不满足时砖块只吸收命中
- 两个阈值作为偏好项开放：`NatoEra Caliber Threshold (mm)`、`NatoEra Penetration Threshold (mm)`
- ERA 砖块引爆时播放爆炸特效与音效，并隐藏砖块（与 Fulda 1989 同款处理）
- 启动日志新增注入位置，便于确认 transpiler 是否命中预期锚点
- 转换后的 M8-LOSAT 载具不再被挂上 ERA

## v1.2.5 — 适配 20261014

- 适配 GHPC 20261014 版本