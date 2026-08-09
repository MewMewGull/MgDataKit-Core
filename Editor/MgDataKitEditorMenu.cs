/// <summary>
/// MgDataKit 自包含的编辑器菜单路径。
/// 插件代码不依赖宿主项目的菜单常量，宿主仍可通过 Unity 菜单看到相同入口。
/// </summary>
public static class MgDataKitEditorMenu {
    public const string Root = "MgDataKit";

    public static class Data {
        public const string MgDataTables = Root + "/数据/MgData 表编辑器 %#m";
        public const string MgDataTableTags = Root + "/数据/MgData 表标签管理...";
        public const string MgDataTableTypeConfiguration = Root + "/数据/MgData 表类型配置...";
        public const string MgDataKitSettings = Root + "/数据/MgDataKit Settings...";
    }
}
