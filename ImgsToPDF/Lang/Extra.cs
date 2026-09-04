using System;
using System.Collections.Concurrent;
using System.Resources;

namespace ImgsToPDF.Lang {
    internal class Extra {
        // ResourceManager 构造涉及资源集定位，缓存实例避免每次调用重复创建
        private static readonly ConcurrentDictionary<Type, ResourceManager> resources = new();

        /// <summary>
        /// 应用资源文件中某个值
        /// </summary>
        /// <param name="resourceObject">指定用哪个界面类的资源文件</param>
        /// <param name="Name">指定用资源文件中的哪个值</param>
        /// <returns></returns>
        internal static string ApplyResource(Type resourceObject, string Name) {
            ResourceManager resource = resources.GetOrAdd(resourceObject, static t => new ResourceManager(t));
            return resource.GetString(Name);
        }
    }
}
