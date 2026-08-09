using System;

namespace MgDataKit {
    public enum EMgDataLintSeverity {
        Warning,
        Error
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MgDataPrimaryKeyAttribute : Attribute {
        public EMgDataLintSeverity DuplicateSeverity { get; set; } = EMgDataLintSeverity.Error;
    }
}
