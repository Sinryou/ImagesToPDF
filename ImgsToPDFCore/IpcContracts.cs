namespace ImgsToPDF.Shared {
    /// <summary>
    /// GUI 宿主与 Core 子进程之间的跨进程通信（IPC）协议与清单常量契约。
    /// 集中管理管道交互指令、看门狗环境变量及批处理清单前缀，避免两端硬编码魔法字符串导致契约漂移。
    /// </summary>
    internal static class IpcContracts {
        /// <summary>
        /// Core 进程请求父进程弹出密码对话框的指令前缀（输出至 stdout）
        /// 格式：::__REQ_PASSWORD__::{archivePath}
        /// </summary>
        public const string ReqPasswordPrefix = "::__REQ_PASSWORD__::";

        /// <summary>
        /// GUI 进程回传 Base64 编码密码的响应前缀（写入至 stdin）
        /// 格式：::__RESP_PASSWORD__::{base64Password}
        /// </summary>
        public const string RespPasswordPrefix = "::__RESP_PASSWORD__::";

        /// <summary>
        /// GUI 进程通知用户取消输入密码或取消解压的操作指令（写入至 stdin）
        /// </summary>
        public const string CancelToken = "::__CANCEL__::";

        /// <summary>
        /// 启用父进程看门狗机制的环境变量名。GUI 拉起 Core 时注入该变量为 "1"
        /// </summary>
        public const string WatchParentEnvVar = "IMGTSPDF_WATCH_PARENT";

        /// <summary>
        /// 离散图片合并清单（Manifest）文件中用于指定最终目标 PDF 输出路径的行前缀
        /// 格式：#OUTPUT:{pdfPath}
        /// </summary>
        public const string ManifestOutputPrefix = "#OUTPUT:";
    }
}
