using System;
using System.Collections.Generic;
using ImgsToPDFCore;
using Microsoft.VisualBasic;
using XLua;

/// <summary>
/// C#内全局使用的变量，同时供Lua调用
/// </summary>
internal struct CSGlobal {
    #region readonlys
    public static readonly LuaEnv luaEnv = new();
    [LuaCallCSharp]
    [ReflectionUse]
    public static readonly List<Type> lua_call_cs_list = [
        typeof(iTextSharp.text.PageSize),
        typeof(iTextSharp.text.Rectangle),
        typeof(Interaction),
        typeof(PDFWrapper),
    ];
    #endregion
    public static IConfig luaConfig;

    /// <summary>
    /// 父进程（GUI）是否已断开标准输入管道（即父进程已关闭或崩溃退出）。
    /// </summary>
    public static volatile bool IsCancelled;

    /// <summary>
    /// 若已收到父进程退出信号，抛出取消异常以中断耗时循环并触发 finally 清理临时文件。
    /// </summary>
    public static void ThrowIfCancelled() {
        if (IsCancelled) {
            throw new OperationCanceledException("The parent process has exited.");
        }
    }
}
