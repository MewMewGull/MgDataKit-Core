# MgDataKit Core

MgDataKit Core 是 Unity 的数据资产与编辑器集成核心层。它不依赖任何具体游戏的数据表定义；游戏项目应在仓库外维护自己的表类和数据源适配器。

## 目录结构

- `Runtime/`：`MgDataBase`、特性、合并辅助方法和通用值类型。数据源元数据属于编辑器侧，由项目 Catalog 管理。
- `Editor/`：Catalog、导入管线、校验、设置、标签和编辑器窗口。
- `Editor/Import/`：与数据源无关的网格映射、导入编排和本地缓存工具。不包含任何具体数据源读取器。

本仓库只包含可复用的核心代码，明确排除项目资产、凭据、外部服务、第三方数据读取器和游戏专用适配器。

## 安装

本仓库符合 Unity Package Manager（UPM）包结构，包名为 `com.mgdatakit.core`，最低支持 Unity 2022.3。

### 从 Git URL 安装

在 Unity 的 Package Manager 中点击 `+` → `Add package from git URL...`，输入仓库地址：

```text
https://github.com/<owner>/MgDataKit-Core.git
```

也可以直接在项目的 `Packages/manifest.json` 中添加：

```json
{
  "dependencies": {
    "com.mgdatakit.core": "https://github.com/<owner>/MgDataKit-Core.git#main"
  }
}
```

正式发布时建议使用 Git tag，例如 `#v0.1.0`，避免分支变化导致项目收到未经验证的代码。

### 从本地目录安装

在 Package Manager 中选择 `+` → `Add package from disk...`，选中本仓库根目录下的 `package.json`。本地包目录必须保留 `package.json`、`Runtime/` 和 `Editor/`。

### 兼容旧的 Assets 复制方式

仍然可以将仓库复制到 Unity 工程的 `Assets/MgDataKit` 目录。核心会自动兼容该路径；但新项目更推荐使用 Package Manager。

安装后，通过 MgDataKit 编辑器菜单创建项目设置和 Catalog 资产。它们属于使用方 Unity 工程，会创建在 `Assets/MgDataKit/Project/`，不会写入 Package Manager 缓存目录。

项目资产直接使用当前字段布局；MgDataKit 不维护 schema 版本字段，也不提供自动结构迁移。每个 Unity 项目都应创建自己的设置和 Catalog 资产。

## 扩展点

可选集成应在仓库外实现 `MgDataKit.Editor.IMgDataSourceAdapter` 和/或 `IMgDataSourceImporter`，将外部数据源转换为通用网格。`IMgDataImportExtension` 用于表级导入处理，`IMgDataSyncOrderProvider` 可用于提供相关表的确定性同步顺序。MgDataKit 会从已加载的编辑器程序集发现这些实现，因此具体集成无需进入核心仓库。

编辑器窗口集成应实现 `IMgDataKitEditorExtension`。扩展可以通过 `IMgDataKitEditorRegistry` 注册操作和空状态视图，也可以提供 `IMgDataKitAssetRowExtension` 实现，用于数据源相关的行 UI。类型标题、类型搜索/筛选区域、类型列表和 Asset 标题仍由核心负责，直到相关扩展契约稳定。

数据源还可以实现 `IMgDataSourceAdapter`。适配器负责稳定的 `SourceId`、绑定校验、数据读取、绑定 UI、打开来源和新建绑定初始化；也可以实现 `IMgDataSourceBatchImportAdapter` 提供数据源专用的批量创建流程。Catalog 条目只保存通用的 `SourceId` 和不透明来源数据，不保留具体数据源的兼容字段。

数据源适配器应与核心导入管线隔离：适配器负责将来源转换为通用网格，表级对账和后处理则放在导入扩展中。这样可以保持稳定、精简的核心 API，方便后续开源集成。

具体适配器可以维护在独立仓库中，并通过上述扩展点被发现，不需要修改核心导入器。
