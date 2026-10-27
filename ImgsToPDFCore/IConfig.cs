using XLua;

namespace ImgsToPDF.Core {
    /// <summary>
    /// Lua内定义的配置属性及方法
    /// </summary>
    [CSharpCallLua]
    public interface IConfig {
        string PathToSave();
        iTextSharp.text.Rectangle PageSizeToSave { get; set; }
        int FilePathComparer(string a, string b);
        void PreProcess(string directoryPath, Layout layout, bool fastFlag, bool merge);
        void PostProcess();
        int FastQuality { get; set; }
    }
}
