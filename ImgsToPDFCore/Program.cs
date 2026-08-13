using CommandLine;
using System;
using XLua;

namespace ImgsToPDFCore {
    internal class Program {
        /// <summary>
        /// 程序的所有命令行参数类型
        /// </summary>
        private class Options {
            [Option('d', "dir-path", Required = true, HelpText = "图片所在的文件夹路径。")]
            public string DirectoryPath { get; set; }

            [Option('l', "layout", Required = false, HelpText = "页面布局，0为单页输出，1为双页左至右，2为双页右至左。")]
            public Layout Layout { get; set; }

            [Option('f', "fast", Required = false, HelpText = "是否以牺牲图片质量换取生成速度。")]
            public bool FastFlag { get; set; }

            [Option('m', "merge-pdfs", Required = false, HelpText = "用于合并pdf的单独功能")]
            public bool Merge { get; set; }
        }
        static void Main(string[] args) {
            //for (int i = 0; i < args.Length; i++) {
            //    Console.WriteLine(i + " " + args[i]);
            //}
            Parser.Default.ParseArguments<Options>(args).WithParsed(Run);
        }
        /// <summary>
        /// 使用解析后的命令行参数进行操作。
        /// </summary>
        /// <param name="option">解析后的参数</param>
        static void Run(Options option) {
            try {
                CSGlobal.luaEnv.AddBuildin("ffi", XLua.LuaDLL.Lua.LoadFFI);
                CSGlobal.luaEnv.AddBuildin("lfs", XLua.LuaDLL.Lua.LoadLFS);
                CSGlobal.luaEnv.AddBuildin("rapidjson", XLua.LuaDLL.Lua.LoadRapidJson);

                CSGlobal.luaEnv.DoString(@"config = require 'config';"); // 获取lua内的方法

                CSGlobal.luaConfig = CSGlobal.luaEnv.Global.Get<IConfig>("config");
                CSGlobal.luaConfig.PreProcess(option.DirectoryPath, option.Layout, option.FastFlag, option.Merge);

                CSGlobal.luaConfig.PostProcess();
            }
            finally {
                // PreProcess 中途抛异常时 PostProcess 不会执行，这里兜底清理临时目录。
                // PostProcess 内部会先检查目录是否存在，重复调用是安全的。
                try {
                    CSGlobal.luaConfig?.PostProcess();
                }
                catch (Exception ex) {
                    Console.Error.WriteLine($"[ImgsToPDFCore] Failed to clean up temp directory: {ex.Message}");
                }
                CSGlobal.luaEnv.Dispose();
            }
        }
    }
}
