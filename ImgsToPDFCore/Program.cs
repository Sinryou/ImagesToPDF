using CommandLine;
using System;

namespace ImgsToPDFCore {
    internal class Program {
        /// <summary>
        /// 程序的所有命令行参数类型
        /// </summary>
        class Options {
            [Option('d', "dir-path", Required = true, HelpText = "图片所在的文件夹路径。")]
            public string DirectoryPath { get; set; }

            [Option('l', "layout", Required = false, HelpText = "页面布局，0为单页输出，1为双页左至右，2为双页右至左。")]
            public int Layout { get; set; }

            [Option('f', "fast", Required = false, HelpText = "是否以牺牲图片质量换取生成速度。")]
            public bool FastFlag { get; set; }
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

            CSGlobal.srcDirPath = option.DirectoryPath; // 给lua调用的
            CSGlobal.luaEnv.DoString(@"config = require 'config';"); // 获取lua内的方法

            CSGlobal.luaConfig = CSGlobal.luaEnv.Global.Get<IConfig>("config");
            CSGlobal.luaConfig.PreProcess();

            //Console.WriteLine((Layout)option.Layout);

            PDFWrapper.ImagesToPDF(CSGlobal.srcDirPath, (Layout)option.Layout, option.FastFlag);

            CSGlobal.luaConfig.PostProcess();

            CSGlobal.luaEnv.Dispose();
        }
    }
}
