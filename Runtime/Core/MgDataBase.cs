using UnityEngine;

namespace MgDataKit {
    /// <summary>
    /// Unity DataAsset 基类。
    /// Editor 导入由 MgDataImportService 按 MgDataKitAssetCatalog 中的数据源分发。
    /// </summary>
    public abstract class MgDataBase : ScriptableObject {
        /// <summary>
        /// 编辑器导入行回调：在行数据解析完成之后调用；
        /// 运行时不调用。可用于额外校验或统计。
        /// </summary>
        public virtual void OnImportedRow(object rowElement) { }
    }
}
