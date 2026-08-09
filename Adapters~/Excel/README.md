# MgDataKit Excel Integration

这是 MgDataKit Core 的可选 Excel 数据源包。它从 `.xlsx` 工作簿读取字符串网格，使用 Core 的通用行映射器导入 `MgDataBase`，并提供：

- Excel 资产绑定、打开来源和 `.xlsx` 自动导入；
- Excel 移动、重命名或删除时自动维护 Catalog 路径；
- NPOI 2.5.6 工作簿读取；
- 通过 Addressables Key 导入 `Sprite`、`AudioClip`、`TimelineAsset` 等 Unity 资源字段；
- Lint 中显示 Excel 来源行号。

## 安装

先安装 Core，再安装本目录包。由于两个包位于同一个 Git 仓库，建议在项目的 `Packages/manifest.json` 中显式声明两项：

`Adapters~/` 是为了让只安装 Core 的项目忽略可选 Excel 程序集；安装本包时请使用下方的 `path`。

```json
{
  "dependencies": {
    "com.mgdatakit.core": "https://github.com/MewMewGull/MgDataKit-Core.git#main",
    "com.mgdatakit.excel": "https://github.com/MewMewGull/MgDataKit-Core.git?path=/Adapters~/Excel#main"
  }
}
```

也可以在 Package Manager 中使用 `Add package from git URL...`，分别添加：

```text
https://github.com/MewMewGull/MgDataKit-Core.git#main
https://github.com/MewMewGull/MgDataKit-Core.git?path=/Adapters~/Excel#main
```

Excel 包需要 Unity 2022.3、Addressables 1.22.3，以及随包提供的 NPOI 二进制和 Apache 2.0 许可证。Excel 源文件和项目 Catalog 仍由使用方工程自行维护，不会包含在本仓库中。
