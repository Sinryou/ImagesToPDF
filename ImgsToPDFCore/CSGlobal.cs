using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using XLua;

namespace ImgsToPDF.Core {
    /// <summary>
    /// C#内全局使用的变量，同时供Lua调用
    /// </summary>
    internal static class CSGlobal {
        #region readonlys
        [LuaCallCSharp]
        [ReflectionUse]
        public static readonly List<Type> lua_call_cs_list = [
            typeof(iTextSharp.text.PageSize),
            typeof(iTextSharp.text.Rectangle),
            typeof(CommonUtils),
            typeof(PDFWrapper),
        ];
        public static readonly LuaEnv luaEnv = new();
        #endregion
        public static IConfig luaConfig;

        /// <summary>
        /// 当前是否由 GUI（父进程）拉起。
        /// </summary>
        public static bool IsGuiMode { get; set; }

        /// <summary>
        /// 父进程（GUI）是否已断开标准输入管道（即父进程已关闭或崩溃退出）。
        /// </summary>
        public static volatile bool IsCancelled;

        /// <summary>
        /// 临时文件与解压目录的清理是否已完成。供看门狗判断：
        /// 清理已完成后不要再用 Environment.Exit 强制结束进程，否则会顶掉真实退出码。
        /// </summary>
        public static volatile bool CleanupCompleted;

        /// <summary>
        /// 存储由看门狗读取线程从标准输入分发而来的交互指令行（如 GUI 返回的密码响应）。
        /// </summary>
        private static readonly BlockingCollection<string> _stdinLineQueue = [];

        /// <summary>
        /// 供看门狗或标准输入读取线程投递接收到的行。传入 null 表示 EOF（管道关闭）。
        /// </summary>
        public static void DeliverStdinLine(string line) {
            if (line == null) {
                _stdinLineQueue.CompleteAdding();
                return;
            }
            if (!_stdinLineQueue.IsAddingCompleted) {
                try {
                    _stdinLineQueue.Add(line);
                }
                catch (InvalidOperationException) {
                    // 队列已关闭
                }
            }
        }

        /// <summary>
        /// 阻塞等待下一行标准输入（带超时与取消感知）。
        /// </summary>
        public static string WaitForStdinLine(TimeSpan timeout) {
            try {
                if (_stdinLineQueue.TryTake(out string line, (int)timeout.TotalMilliseconds)) {
                    return line;
                }
            }
            catch (InvalidOperationException) {
                // 队列已完成添加且已取空
            }
            return null;
        }

        /// <summary>
        /// 若已收到父进程退出信号，抛出取消异常以中断耗时循环并触发 finally 清理临时文件。
        /// </summary>
        public static void ThrowIfCancelled() {
            if (IsCancelled) {
                throw new OperationCanceledException("The parent process has exited.");
            }
        }
    }
}