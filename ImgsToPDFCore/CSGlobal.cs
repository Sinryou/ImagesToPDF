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
    public static readonly LuaEnv luaEnv = new LuaEnv();
    [LuaCallCSharp]
    [ReflectionUse]
    public static readonly List<Type> lua_call_cs_list = new List<Type>() {
        typeof(iTextSharp.text.PageSize),
        typeof(iTextSharp.text.Rectangle),
        typeof(Interaction),
        typeof(PDFWrapper),
    };
    #endregion
    public static IConfig luaConfig;
}
