using XLua;

namespace ImgsToPDFCore {
    /// <summary>
    /// Lua内定义的配置属性及方法
    /// </summary>
    [CSharpCallLua]
    public interface IConfig {
        string PathToSave();
        iTextSharp.text.Rectangle PageSizeToSave { get; set; }
        int FilePathComparer(string a, string b);
        void PreProcess(Program.Options option);
        void PostProcess();
    }
}
