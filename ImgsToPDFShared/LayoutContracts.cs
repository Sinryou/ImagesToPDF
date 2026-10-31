namespace ImgsToPDF.Shared {
    /// <summary>
    /// PDF 输出版式：GUI 下拉框与 Core -l 命令行参数共同引用的唯一定义。
    /// 通过共享项目分别编译进 GUI 与 Core 程序集，任何一端的改动都会同步生效，
    /// 消除此前「GUI 传 SelectedIndex、Core 按顺序解释数值」的隐式契约。
    /// GUI 进程间传参时传枚举名（如 DuplexLeftToRight），Core 侧 CommandLineParser
    /// 按 Enum.Parse 解析，同时向后兼容旧脚本的数值写法（0/1/2）。
    /// </summary>
    public enum Layout {
        /// <summary>单页输出：一张图片一页。</summary>
        Single = 0,
        /// <summary>双页拼接，从左至右排布。</summary>
        DuplexLeftToRight = 1,
        /// <summary>双页拼接（小说模式），从右至左排布。</summary>
        DuplexRightToLeft = 2
    }
}
